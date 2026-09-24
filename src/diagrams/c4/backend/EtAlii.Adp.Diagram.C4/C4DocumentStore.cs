using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.C4;

/// <inheritdoc cref="IC4DocumentStore" />
/// <remarks>
/// <b>A refused read is retried before it is believed</b>, exactly as backend-centralization's
/// <c>DocumentLifecycle</c> does it - five tries 50 ms apart, a missing body retried on a reload but
/// not on a first open, the attempt count logged whenever a retry was needed, and the last good
/// model kept only once the retries are spent. It is a copy on purpose: that task converts this store
/// to the lifecycle, and the conversion should change where this code lives, not how it behaves. The
/// defect it closes is the EditorResolution 60-second flake - saved, the reload's read refused by
/// another holder, the last good model kept, and no later event to re-read it. <b>A retry narrows
/// that window and does not close it</b>: a hold longer than the retries still loses the change.
/// </remarks>
public sealed class C4DocumentStore : IC4DocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<C4DocumentStore>();

    // DocumentLifecycle's numbers, unchanged: a refusal is believed after about a fifth of a second.
    // A choice rather than a measurement, because what holds the file is not yet known - which is
    // why a read that needed a retry logs how many it took.
    private const int DefaultReadAttempts = 5;
    private static readonly TimeSpan DefaultBetweenReadAttempts = TimeSpan.FromMilliseconds(50);

    private readonly ConcurrentDictionary<string, C4DocumentEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing right now, so its own save does not bounce back through
    // Reload as an "external" change - PlainEditorSession's saving guard, per path.
    private readonly ConcurrentDictionary<string, byte> _selfWrites = new(StringComparer.OrdinalIgnoreCase);

    // The paths whose last read FAILED - not missing, which is a new document, but present and
    // refused. Their entry is an empty document standing in for content nobody could read, so
    // Save must not write it: measured, one reload that could not read the body followed by a
    // save left a real .dsl file empty on disk.
    private readonly ConcurrentDictionary<string, byte> _unreadable = new(StringComparer.OrdinalIgnoreCase);

    private readonly Func<string, string> _read;
    private readonly int _readAttempts;
    private readonly TimeSpan _betweenReadAttempts;

    public C4DocumentStore()
        : this(SharedDocumentReader.ReadAllText, DefaultReadAttempts, DefaultBetweenReadAttempts)
    {
    }

    /// <summary>
    /// With the read and its retry supplied, so a test can refuse exactly as often as it chooses and
    /// wait for nothing - deterministic rather than patient.
    /// </summary>
    internal C4DocumentStore(Func<string, string> read, int readAttempts, TimeSpan betweenReadAttempts)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(readAttempts, 1);
        _read = read;
        _readAttempts = readAttempts;
        _betweenReadAttempts = betweenReadAttempts;
    }

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

        // A RELOAD THAT CANNOT READ KEEPS THE LAST GOOD MODEL - once the retries are spent. A body
        // that is missing or refused at the moment of a reload is far more often a publish in flight
        // - a File.Replace renames the body away for an instant - than a model that has gone:
        // measured, an external writer republishing an unchanged .dsl while reloads ran installed an
        // EMPTY workspace in 765 of 3000 of them. Do not assume a later event will re-read it: this
        // may have been the write's last one, and then the change is lost here - which is why the
        // read is retried first. A body that is really gone arrives as BodyDeleted. A first load
        // keeps its old meaning (a body that does not exist yet is a new, empty document), because
        // there is no good model to keep.
        //
        // ONE READ, and the entry is built from it. Checking readability first and then letting
        // Load read again left a window between the two reads: the body vanished in it, Load took
        // the missing body for a NEW document, and the empty model went in anyway - 299 of 3000
        // reloads still lost the model with the check in place, caught by the race guard.
        if (!TryRead(path, retryMissing: true, out var text, out var unavailability, out var failure))
        {
            if (_entries.ContainsKey(path))
            {
                _logger.Warning(failure, "Keeping the last good {Path}: this reload could not read it ({Unavailability})", path, unavailability);
                return;
            }

            // Nothing loaded yet, so nothing good to keep: a first load's own rules apply, to the
            // read just made rather than to a second one.
            var first = _entries[path] = Unavailable(path, unavailability);
            Changed?.Invoke(this, new C4DocumentChangedEventArgs(path, first.Workspace));
            return;
        }

        var entry = Parsed(text);
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

    private C4DocumentEntry Load(string path)
    {
        // A body that does not exist yet is an empty document, not an error: the .adp file may
        // have been created a moment ago, and a diagram that cannot open at all is a worse answer
        // than an empty one. So a missing body is not retried here - retrying it would delay every
        // new diagram - while a refused one is. A body that EXISTS but still cannot be read also
        // opens as empty, but is marked, so that emptiness is never written back over the file.
        if (TryRead(path, retryMissing: false, out var text, out var unavailability, out var failure))
        {
            _unreadable.TryRemove(path, out _);
            return Parsed(text);
        }

        if (unavailability == Unavailability.Unreadable)
        {
            _logger.Warning(failure, "Could not read {Path}; opening it as unavailable", path);
        }

        return Unavailable(path, unavailability);
    }

    private C4DocumentEntry Unavailable(string path, Unavailability unavailability)
    {
        if (unavailability == Unavailability.Unreadable)
        {
            _unreadable[path] = 1;
        }
        else
        {
            _unreadable.TryRemove(path, out _);
        }

        return Parsed("");
    }

    private static C4DocumentEntry Parsed(string text)
    {
        var document = C4Document.Parse(text);
        return new C4DocumentEntry(document, C4Parser.Parse(document));
    }

    /// <summary>
    /// Reads the body, trying again while it is refused - and, when <paramref name="retryMissing"/>,
    /// while it is missing - and answering false once the attempts are spent. What a missing body
    /// MEANS is the caller's call: a first load treats it as a new document, a reload as a publish
    /// in flight.
    /// </summary>
    private bool TryRead(string path, bool retryMissing, out string text, out Unavailability unavailability, out Exception? failure)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (TryReadOnce(path, out text, out unavailability, out failure))
            {
                if (attempt > 1)
                {
                    _logger.Information("Read {Path} on attempt {Attempt} of {Attempts}", path, attempt, _readAttempts);
                }

                return true;
            }

            var worthRetrying = unavailability == Unavailability.Unreadable || retryMissing;
            if (!worthRetrying || attempt >= _readAttempts)
            {
                return false;
            }

            Thread.Sleep(_betweenReadAttempts);
        }
    }

    private bool TryReadOnce(string path, out string text, out Unavailability unavailability, out Exception? failure)
    {
        text = "";
        unavailability = Unavailability.Missing;
        failure = null;

        // The existence check is load-bearing: SharedDocumentReader opens with FileMode.Open and
        // throws on a missing file, and a missing body is a state, not a failure to log.
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            text = _read(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // There when asked, gone when opened: a publish renaming it away for an instant, or a
            // delete. Either way it is missing now, and missing is not an error.
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            unavailability = Unavailability.Unreadable;
            failure = exception;
            return false;
        }
    }

    /// <summary>Why a read found nothing: DocumentLifecycle's DocumentUnavailability, until task 6 brings it.</summary>
    private enum Unavailability
    {
        Missing,
        Unreadable,
    }
}
