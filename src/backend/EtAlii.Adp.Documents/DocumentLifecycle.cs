using System.Collections.Concurrent;
using Serilog;

namespace EtAlii.Adp.Documents;

public class DocumentLifecycle
{
    // Five tries, 50 ms apart: a refusal is believed after about a fifth of a second. Chosen for a
    // sharing violation just after a save, which may be held longer than the microsecond-wide missing
    // file PlainEditorSession retries for (3 x 20 ms) - so it is wider than that, and it is a choice,
    // not a measurement.
    protected const int DefaultReadAttempts = 5;
    protected static readonly TimeSpan DefaultBetweenReadAttempts = TimeSpan.FromMilliseconds(50);
}

/// <summary>
/// One loaded document per body path: opening it, handing it out, forgetting it, and re-reading it
/// when the file changes (backend-centralization R2). A module supplies how its text parses and,
/// if it reports its own unavailability, what an unavailable document looks like; nothing else.
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only on purpose.</b> This type has no save and no self-write bookkeeping, because a
/// store that never writes has nothing to suppress - sparql's lifecycle is this one, and its save
/// bookkeeping is ABSENT rather than a no-op that could be mistaken for a working guard. Writable
/// stores hold a <see cref="WritableDocumentLifecycle{TDocument}"/>, which composes this one.
/// </para>
/// <para>
/// <b>A reload that cannot read keeps the last good document (R2.4), for every store.</b> A read
/// that fails is far more often another program's publish in flight than a document that has gone:
/// c4 measured an external writer republishing an unchanged body while reloads ran, and installing
/// the failure put an EMPTY workspace on the canvas in 765 of 3000 reloads. Before this type, two of
/// the ten module stores kept the last good document; six installed an empty one, mindmap threw,
/// and sparql replaced a good query with an error. A module's declaration that it reports its own
/// unavailability governs the FIRST load only (R2.2), where there is no last good document to keep.
/// </para>
/// <para>
/// <b>A refused read is retried before it is believed, because the last event has no successor.</b>
/// Keep-last-good was first justified by "the watcher's next event re-reads the finished file". That
/// is true while a publish is in flight, and false when the refusal lands on the LAST event of a
/// write: nothing follows to re-read it, so the change is lost for good rather than late. Developer 5
/// traced the EditorResolution 60-second flake to exactly that in c4's store: saved, read refused by
/// another holder, last good kept, and no further event (2026-09-25). So a read that is refused, or
/// that finds the body missing during a reload, is tried again a few times before anything is
/// concluded, as <c>PlainEditorSession.ReadWithRetry</c> already does. <b>Retrying narrows that window
/// and does not close it</b>: a hold longer than the retries still loses the change. The window is a
/// choice rather than a measurement, because what held the file is not yet known, so a read that
/// needed a retry logs how many it took. Those log lines are how the real hold gets measured.
/// </para>
/// <para>
/// <b>Absence is confirmed by the watcher, never inferred from a read</b> (R2.5): a body missing
/// on a reload is kept, exactly like an unreadable one, and becomes the empty document only through
/// <see cref="BodyDeleted"/>. That pairing is the point - see <see cref="BodyDeleted"/>.
/// </para>
/// </remarks>
/// <typeparam name="TDocument">
/// What the store caches: an entry record for most modules, the document itself for mindmap and
/// wardley-map.
/// </typeparam>
public sealed class DocumentLifecycle<TDocument> : DocumentLifecycle
    where TDocument : class
{
    private static readonly ILogger _logger = Log.ForContext<DocumentLifecycle<TDocument>>();

    private readonly ConcurrentDictionary<string, TDocument> _documents = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, string, TDocument> _parse;
    private readonly Func<string, DocumentUnavailability, string, TDocument>? _unavailable;
    private readonly Func<string, string> _read;
    private readonly int _readAttempts;
    private readonly TimeSpan _betweenReadAttempts;

    /// <param name="parse">
    /// Turns a body path and its text into the cached document. Given <c>""</c> for a body that is
    /// missing or unreadable, unless <paramref name="unavailable"/> is supplied.
    /// </param>
    /// <param name="unavailable">
    /// For a module that reports a missing or unreadable body as a state of its own rather than
    /// opening it empty (R2.2 - causal-loop and sparql today): builds that document from the path,
    /// which case it is, and the reason the read failed (empty for a missing body).
    /// </param>
    public DocumentLifecycle(
        Func<string, string, TDocument> parse,
        Func<string, DocumentUnavailability, string, TDocument>? unavailable = null)
        : this(parse, unavailable, SharedDocumentReader.ReadAllText, DefaultReadAttempts, DefaultBetweenReadAttempts)
    {
    }

    /// <summary>
    /// With the read and its retry supplied, so a test can refuse exactly as often as it chooses and
    /// wait for nothing - deterministic rather than patient.
    /// </summary>
    internal DocumentLifecycle(
        Func<string, string, TDocument> parse,
        Func<string, DocumentUnavailability, string, TDocument>? unavailable,
        Func<string, string> read,
        int readAttempts,
        TimeSpan betweenReadAttempts)
    {
        ArgumentNullException.ThrowIfNull(parse);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThan(readAttempts, 1);
        _parse = parse;
        _unavailable = unavailable;
        _read = read;
        _readAttempts = readAttempts;
        _betweenReadAttempts = betweenReadAttempts;
    }

    /// <summary>The document at <paramref name="path"/>, opened once and kept.</summary>
    /// <remarks>
    /// A missing body opens as a new document; an unreadable one opens the same way with a warning
    /// naming the path (R2.2). A parse that throws is not caught: it is that document's failure,
    /// raised to whoever opened it, and nothing is cached for it.
    /// </remarks>
    public TDocument GetOrLoad(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _documents.GetOrAdd(path, Open);
    }

    /// <summary>The loaded document, or null when nothing has opened it; never loads.</summary>
    public TDocument? Get(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return _documents.TryGetValue(path, out var document) ? document : null;
    }

    /// <summary>Drops a loaded document, so the next open reads the file afresh.</summary>
    public void Forget(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _documents.TryRemove(path, out _);
    }

    /// <summary>Re-reads a body changed on disk.</summary>
    /// <returns>
    /// <c>true</c> when a new document was installed, so the store tells its sessions; <c>false</c>
    /// when the last good one was kept and nothing changed for them.
    /// </returns>
    /// <remarks>
    /// The new document is parsed BEFORE it replaces the cached one, so a parse that throws leaves
    /// the last good document in place and fails this reload alone - one document's failure never
    /// costs another's.
    /// </remarks>
    public bool Reload(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!TryRead(path, retryMissing: true, out var text, out var unavailability, out var failure))
        {
            if (_documents.ContainsKey(path))
            {
                // A RELOAD THAT CANNOT READ KEEPS THE LAST GOOD DOCUMENT (R2.4) - but only once the
                // retries are spent. Do not assume a later event will re-read it: this may have been the
                // write's last one, and then the change is lost here. A body that is really gone
                // arrives as BodyDeleted.
                _logger.Warning(failure, "Keeping the last good {Path}: this reload could not read it ({Unavailability})", path, unavailability);
                return false;
            }

            // Nothing loaded, so nothing good to keep: a first open's own rules apply.
            _documents[path] = Unavailable(path, unavailability, failure);
            return true;
        }

        var reloaded = _parse(path, text);
        _documents[path] = reloaded;
        return true;
    }

    /// <summary>
    /// The body was deleted - the watcher's evidence, not a read that failed - so the document ends
    /// as a first open of a missing body would leave it, not as the last one kept alive (R2.5).
    /// </summary>
    /// <returns><c>true</c> when a document was installed, as for <see cref="Reload"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>This and keep-last-good arrive together, or a deleted diagram is never cleared.</b>
    /// <c>IDiagramDocumentReloader.BodyDeleted</c> defaults to a reload, which was right only while
    /// a failed read installed an empty document. Once a reload keeps the last good one, a delete
    /// routed to it would keep it forever - so a store converted to this type must forward the
    /// bridge's <c>BodyDeleted</c> here, in the same change.
    /// </para>
    /// <para>
    /// A body already back - an editor that saves by deleting and re-creating - is re-read instead,
    /// because what is on disk now is the answer rather than the delete that preceded it.
    /// </para>
    /// </remarks>
    public bool BodyDeleted(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (File.Exists(path))
        {
            return Reload(path);
        }

        _documents[path] = Unavailable(path, DocumentUnavailability.Missing, failure: null);
        return true;
    }

    /// <summary>
    /// Puts <paramref name="document"/> in the cache for <paramref name="path"/> - the document a
    /// save was handed, never one looked up here.
    /// </summary>
    internal void Install(string path, TDocument document) => _documents[path] = document;

    /// <summary>
    /// The text <paramref name="path"/> holds on disk right now, read once and without touching the
    /// cache - for a save that must know whether the file moved under the edit it is about to write.
    /// </summary>
    /// <returns>False for a body that is missing or cannot be read; the save then has nothing to compare.</returns>
    internal bool TryReadCurrentText(string path, out string text) => TryReadOnce(path, out text, out _, out _);

    /// <summary>Parses <paramref name="text"/> as the document at <paramref name="path"/>, without caching it.</summary>
    internal TDocument Parse(string path, string text) => _parse(path, text);

    private TDocument Open(string path)
    {
        // A missing body on a first open is a new document rather than a publish in flight, so only a
        // refused read is retried here - retrying a missing one would delay every new diagram.
        if (TryRead(path, retryMissing: false, out var text, out var unavailability, out var failure))
        {
            return _parse(path, text);
        }

        if (unavailability == DocumentUnavailability.Unreadable)
        {
            _logger.Warning(failure, "Could not read {Path}; opening it as unavailable", path);
        }

        return Unavailable(path, unavailability, failure);
    }

    private TDocument Unavailable(string path, DocumentUnavailability unavailability, Exception? failure) =>
        _unavailable is null
            ? _parse(path, "")
            : _unavailable(path, unavailability, failure?.Message ?? "");

    private bool TryRead(string path, bool retryMissing, out string text, out DocumentUnavailability unavailability, out Exception? failure)
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

            var worthRetrying = unavailability == DocumentUnavailability.Unreadable || retryMissing;
            if (!worthRetrying || attempt >= _readAttempts)
            {
                return false;
            }

            Thread.Sleep(_betweenReadAttempts);
        }
    }

    private bool TryReadOnce(string path, out string text, out DocumentUnavailability unavailability, out Exception? failure)
    {
        text = "";
        unavailability = DocumentUnavailability.Missing;
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
            unavailability = DocumentUnavailability.Unreadable;
            failure = exception;
            return false;
        }
    }
}
