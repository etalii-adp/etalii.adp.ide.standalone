using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <inheritdoc cref="ICausalLoopDocumentStore" />
public sealed class CausalLoopDocumentStore : ICausalLoopDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<CausalLoopDocumentStore>();

    private readonly ConcurrentDictionary<string, CausalLoopDocumentEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - timeline's and c4's saving guard, per path. Without it a
    // reload landing inside the store's own File.Replace found the body missing and installed an
    // Unreadable entry: 18429 of 141188 reloads racing 2000 saves damaged the document in
    // CausalLoopDocumentStoreSelfWriteTests, on a develop that already serialised the saves.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public event EventHandler<CausalLoopDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public CausalLoopDocumentEntry GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _entries.GetOrAdd(path, Load);
    }

    /// <inheritdoc />
    public string Save(string path, CausalLoopDocumentEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entry);

        // THE ENTRY WRITTEN IS THE ONE THE CALLER EDITED, never one re-fetched from the cache.
        // Taking it as an argument is the enforcement: there is no cache read left here to get
        // wrong. Until this signature changed, Save looked the entry up itself while Reload
        // replaces _entries[path], so a reload landing between a command's edit and its save
        // wrote the re-read file and returned "" for success - the command's inverse then went
        // onto the undo stack for a change the file never received. Measured in six other stores
        // and fixed at 6c4f90d6; this store, c4 and mindmap were missed there because their
        // saves re-fetch through a differently named helper rather than through GetOrLoad.
        if (!entry.IsUsable)
        {
            // AN ENTRY THAT COULD NOT BE READ IS NOT A DOCUMENT TO WRITE. Its document is empty, and
            // writing it replaced a real diagram on disk with an empty file - measured: one reload
            // that could not read the body, then a save, and the .cld was gone. Timeline's store
            // refuses the same way. The file on disk is the only copy left; leave it alone.
            _logger.Warning("Refusing to write {Path}: it could not be read ({Error})", path, entry.Error);
            return $"This causal loop diagram could not be read, so it was not written. {entry.Error}";
        }

        _selfWrites[path] = 1;
        try
        {
            // The central writer rather than a raw write: it publishes through a scratch file so
            // a reader never sees a half-written document, which a guard enforces tree-wide.
            AdpFileWriter.Save(path, entry.Document.Text);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"This causal loop diagram could not be saved: {exception.Message}";
        }
        finally
        {
            _selfWrites.TryRemove(path, out _);
        }

        // Re-parsed from the document just written, so the model and the bytes cannot disagree.
        // This also re-establishes the cache around the entry that was just written, which is what
        // the removed "is not loaded" refusal used to stand in for: the caller now holds the entry,
        // so there is no unloaded case left for the store to discover.
        var parsed = CausalLoopParser.Parse(entry.Document);
        _entries[path] = entry with { Model = parsed.Model, Problems = parsed.Problems };
        return "";
    }

    /// <inheritdoc />
    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _entries.TryRemove(path, out _);
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_selfWrites.ContainsKey(path))
        {
            // The change on disk is this store's own save, mid-write; Save has already re-parsed
            // the document it wrote, so there is nothing to re-read.
            return;
        }

        var reloaded = Load(path);
        if (!reloaded.IsUsable && _entries.TryGetValue(path, out var previous) && previous.IsUsable)
        {
            // A RELOAD THAT CANNOT READ KEEPS THE LAST GOOD DOCUMENT. A read that fails is far more
            // often a publish in flight - another program's File.Replace renames the body away for
            // a moment - than a diagram that has become unreadable, and the watcher's next event
            // re-reads the finished file. Installing the failure instead lost the diagram on the
            // canvas and, until Save learned to refuse it, on disk.
            _logger.Warning("Keeping the last good {Path}: this reload could not read it ({Error})", path, reloaded.Error);
            return;
        }

        _entries[path] = reloaded;
        Changed?.Invoke(this, new CausalLoopDocumentChangedEventArgs(path));
    }

    /// <inheritdoc />
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

        // GONE, BY THE WATCHER'S OWN EVIDENCE, so the last good diagram is not kept alive. A missing
        // body reads as Unreadable here, as it does on a first open, and Save refuses it.
        _entries[path] = Load(path);
        Changed?.Invoke(this, new CausalLoopDocumentChangedEventArgs(path));
    }

    private static CausalLoopDocumentEntry Load(string path)
    {
        string text;
        try
        {
            // The central reader rather than File.ReadAllText: a raw read opens at
            // FileShare.Read and loses to a concurrent save, which a guard in the backend
            // suite enforces across every production file.
            text = SharedDocumentReader.ReadAllText(path);
        }
        catch (IOException exception)
        {
            return CausalLoopDocumentEntry.Unreadable($"This causal loop diagram could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return CausalLoopDocumentEntry.Unreadable($"This causal loop diagram could not be read: {exception.Message}");
        }

        var document = CausalLoopDocument.Parse(text);
        var parsed = CausalLoopParser.Parse(document);
        return new CausalLoopDocumentEntry(document, parsed.Model, parsed.Problems, "");
    }
}
