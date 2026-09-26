using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <inheritdoc cref="ICausalLoopDocumentStore" />
/// <remarks>
/// <b>A refused read is retried before it is believed</b>, exactly as backend-centralization's
/// <c>DocumentLifecycle</c> does it - five tries 50 ms apart, a missing body retried on a reload but
/// not on a first open, the attempt count logged whenever a retry was needed, and the last good
/// diagram kept only once the retries are spent. It is a copy on purpose: that task converts this
/// store to the lifecycle, and the conversion should change where this code lives, not how it
/// behaves. The defect it closes is c4's EditorResolution 60-second flake, whose shape this store
/// shares - saved, the reload's read refused by another holder, the last good diagram kept, and no
/// later event to re-read it. <b>A retry narrows that window and does not close it</b>: a hold
/// longer than the retries still loses the change.
/// </remarks>
public sealed class CausalLoopDocumentStore : ICausalLoopDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<CausalLoopDocumentStore>();

    // DocumentLifecycle's numbers, unchanged: a refusal is believed after about a fifth of a second.
    // A choice rather than a measurement, because what holds the file is not yet known - which is
    // why a read that needed a retry logs how many it took.
    internal const int DefaultReadAttempts = 5;
    private static readonly TimeSpan DefaultBetweenReadAttempts = TimeSpan.FromMilliseconds(50);

    private readonly ConcurrentDictionary<string, CausalLoopDocumentEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    // The paths this store is writing, and what it last wrote to each, so its own save does not
    // bounce back through Reload as an "external" change - timeline's and c4's saving guard, per
    // path. Without it a reload landing inside the store's own File.Replace found the body missing
    // and installed an Unreadable entry: 18429 of 141188 reloads racing 2000 saves damaged the
    // document in CausalLoopDocumentStoreSelfWriteTests, on a develop that already serialised the
    // saves.
    private readonly SelfWriteGuard _selfWrites = new();

    private readonly Func<string, string> _read;
    private readonly int _readAttempts;
    private readonly TimeSpan _betweenReadAttempts;

    public CausalLoopDocumentStore()
        : this(SharedDocumentReader.ReadAllText, DefaultReadAttempts, DefaultBetweenReadAttempts)
    {
    }

    /// <summary>
    /// With the read and its retry supplied, so a test can refuse exactly as often as it chooses and
    /// wait for nothing - deterministic rather than patient.
    /// </summary>
    internal CausalLoopDocumentStore(Func<string, string> read, int readAttempts, TimeSpan betweenReadAttempts)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(readAttempts, 1);
        _read = read;
        _readAttempts = readAttempts;
        _betweenReadAttempts = betweenReadAttempts;
    }

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

        _selfWrites.Begin(path, entry.Document.Text);
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
            _selfWrites.End(path);
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
        _selfWrites.Forget(path);
    }

    /// <inheritdoc />
    public void Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_selfWrites.IsOwnWrite(path))
        {
            // The change on disk is this store's own save, in flight or already landed; Save has
            // already re-parsed the document it wrote, so there is nothing to re-read.
            return;
        }

        var read = TryRead(path, retryMissing: true, out var text, out var unavailability, out var failure);
        if (!read && _entries.TryGetValue(path, out var previous) && previous.IsUsable)
        {
            // A RELOAD THAT CANNOT READ KEEPS THE LAST GOOD DOCUMENT - once the retries are spent. A
            // read that fails is far more often a publish in flight - another program's File.Replace
            // renames the body away for a moment - than a diagram that has become unreadable.
            // Installing the failure instead lost the diagram on the canvas and, until Save learned
            // to refuse it, on disk. Do not assume a later event will re-read it: this may have been
            // the write's last one, and then the change is lost here - which is why the read is
            // retried first. A body that is really gone arrives as BodyDeleted.
            _logger.Warning(failure, "Keeping the last good {Path}: this reload could not read it ({Unavailability})", path, unavailability);
            return;
        }

        _entries[path] = read ? Parsed(text) : Unreadable(failure);
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

    private CausalLoopDocumentEntry Load(string path)
    {
        // A missing body on a first open is not retried - retrying it would delay every new diagram
        // - while a refused one is.
        if (TryRead(path, retryMissing: false, out var text, out var unavailability, out var failure))
        {
            return Parsed(text);
        }

        if (unavailability == Unavailability.Unreadable)
        {
            _logger.Warning(failure, "Could not read {Path}; opening it as unavailable", path);
        }

        return Unreadable(failure);
    }

    private static CausalLoopDocumentEntry Parsed(string text)
    {
        var document = CausalLoopDocument.Parse(text);
        var parsed = CausalLoopParser.Parse(document);
        return new CausalLoopDocumentEntry(document, parsed.Model, parsed.Problems, "");
    }

    private static CausalLoopDocumentEntry Unreadable(Exception? failure) =>
        CausalLoopDocumentEntry.Unreadable($"This causal loop diagram could not be read: {failure?.Message}");

    /// <summary>
    /// Reads the body, trying again while it is refused - and, when <paramref name="retryMissing"/>,
    /// while it is missing - and answering false once the attempts are spent.
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

    /// <remarks>
    /// DocumentLifecycle's classification, with one difference kept from this store: a missing body
    /// keeps its exception, because this module reports a missing body in the reader's own words
    /// rather than opening it empty. So there is no existence check first - the reader's
    /// FileNotFoundException is the missing case.
    /// </remarks>
    private bool TryReadOnce(string path, out string text, out Unavailability unavailability, out Exception? failure)
    {
        text = "";
        unavailability = Unavailability.Missing;
        failure = null;

        try
        {
            // The central reader rather than File.ReadAllText: a raw read opens at
            // FileShare.Read and loses to a concurrent save, which a guard in the backend
            // suite enforces across every production file.
            text = _read(path);
            return true;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // There when asked, gone when opened: a publish renaming it away for an instant, or a
            // delete. Either way it is missing now.
            failure = exception;
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
