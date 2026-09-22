using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <inheritdoc cref="IC4DocumentStore" />
public sealed class C4DocumentStore : IC4DocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<C4DocumentStore>();

    private readonly ConcurrentDictionary<string, C4DocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    // The paths whose last read FAILED - not missing, which is a new document, but present and
    // refused. Their entry is an empty document standing in for content nobody could read, so
    // Save must not write it: measured, one reload that could not read the body followed by a
    // save left a real .dsl file empty on disk.
    private readonly ConcurrentDictionary<string, byte> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<C4DocumentChangedEventArgs>? Changed;

    public C4Document GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Loaded(path).Document;
    }

    public C4Workspace WorkspaceOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Loaded(path).Workspace;
    }

    public string Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var entry = Loaded(path);
        if (_unreadable.ContainsKey(path))
        {
            // AN ENTRY STANDING IN FOR A FILE THAT COULD NOT BE READ IS NOT A DOCUMENT TO WRITE:
            // the file on disk is the only copy of the model left.
            _logger.Warning("Refusing to write {Path}: it could not be read", path);
            return $"{Path.GetFileName(path)} could not be read, so it was not written.";
        }

        var text = entry.Document.ToText();
        _selfWrites[path] = 1;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (directory is { Length: > 0 } && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            AdpFileWriter.Save(path, text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The edit stays in memory: losing it because the disk refused would be worse than
            // a save the user can retry once the file is writable again.
            _logger.Warning(exception, "Could not write {Path}; the change is kept in memory", path);

            // Reported rather than swallowed. A bare return out of a void Save left the
            // caller answering success while the file still held the old content - the
            // defect found in WardleyDocumentStore and identical here.
            return $"{Path.GetFileName(path)} could not be written. The change is still here to try again.";
        }
        finally
        {
            _selfWrites.TryRemove(path, out _);
        }

        var workspace = C4Parser.Parse(entry.Document);
        _entries[path] = new C4DocumentEntry(entry.Document, workspace);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, workspace));
        return "";
    }

    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, Loaded(path).Workspace));
    }

    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _entries.TryRemove(path, out _);
    }

    /// <summary>Re-reads a document an external tool changed, and tells the sessions on it.</summary>
    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_selfWrites.ContainsKey(path))
        {
            // The change on disk is this store's own save, mid-write; Save reparses and tells
            // the sessions itself.
            return;
        }

        // A RELOAD THAT CANNOT READ KEEPS THE LAST GOOD MODEL. A body that is missing or refused at
        // the moment of a reload is far more often a publish in flight - a File.Replace renames the
        // body away for an instant - than a model that has gone: measured, an external writer
        // republishing an unchanged .dsl while reloads ran installed an EMPTY workspace in 765 of
        // 3000 of them. The watcher's next event re-reads the finished file. A first load keeps its
        // old meaning (a body that does not exist yet is a new, empty document), because there is
        // no good model to keep.
        //
        // ONE READ, and the entry is built from it. Checking readability first and then letting
        // Load read again left a window between the two reads: the body vanished in it, Load took
        // the missing body for a NEW document, and the empty model went in anyway - 299 of 3000
        // reloads still lost the model with the check in place, caught by the race guard.
        if (!TryRead(path, out var text))
        {
            if (_entries.ContainsKey(path))
            {
                _logger.Warning("Keeping the last good {Path}: this reload could not read it", path);
                return;
            }

            // Nothing loaded yet, so nothing good to keep: a first load's own rules apply.
            var first = Loaded(path);
            Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, first.Workspace));
            return;
        }

        var document = C4Document.Parse(text);
        var entry = new C4DocumentEntry(document, C4Parser.Parse(document));
        _entries[path] = entry;
        _unreadable.TryRemove(path, out _);
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, entry.Workspace));
    }

    /// <summary>The body was deleted: the model ends empty, and the sessions on it are told.</summary>
    public void BodyDeleted(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path))
        {
            // Already back - an editor that saves by deleting and re-creating - so what is on
            // disk now is the answer, not the delete that preceded it.
            Reload(path);
            return;
        }

        // GONE, BY THE WATCHER'S OWN EVIDENCE, so the last good model is not kept alive: Load opens
        // a missing body as a new, empty document, and a save of it creates the file again.
        var entry = Load(path);
        _entries[path] = entry;
        Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, entry.Workspace));
    }

    private C4DocumentEntry Loaded(string path) => _entries.GetOrAdd(path, Load);

    /// <summary>
    /// Reads the body, answering false when it is missing or cannot be read. What a missing body
    /// MEANS is the caller's call: a first load treats it as a new document, a reload as a publish
    /// in flight.
    /// </summary>
    private static bool TryRead(string path, out string text)
    {
        text = "";
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            text = SharedDocumentReader.ReadAllText(path);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(exception, "Could not read {Path}", path);
            return false;
        }
    }

    private C4DocumentEntry Load(string path)
    {
        // A body that does not exist yet is an empty document, not an error: the .adp file may
        // have been created a moment ago, and a diagram that cannot open at all is a worse answer
        // than an empty one. A body that EXISTS but cannot be read also opens as empty - but is
        // marked, so that emptiness is never written back over the file.
        var read = TryRead(path, out var text);
        if (read || !File.Exists(path))
        {
            _unreadable.TryRemove(path, out _);
        }
        else
        {
            _unreadable[path] = 1;
        }

        var document = C4Document.Parse(text);
        return new C4DocumentEntry(document, C4Parser.Parse(document));
    }

}
