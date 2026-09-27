using EtAlii.Adp.Documents;
using Serilog;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <inheritdoc cref="ICausalLoopDocumentStore" />
/// <remarks>
/// <para>
/// <b>A thin use of the shared lifecycle (backend-centralization task 6).</b> Opening, the retries
/// before a refused read is believed, keeping the last good diagram through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save
/// (R2.3) and creating the folder a first save needs are all
/// <see cref="WritableDocumentLifecycle{TDocument}"/>'s. What stays here is what is this module's: how
/// a body's text becomes an entry, the unreadable entry a body that could not be read opens as
/// (R2.2), the refusal to write that entry, and telling the sessions.
/// </para>
/// <para>
/// <b>Three behaviours changed with the conversion, by the user's ruling on the decision card
/// (2026-09-26), to the lifecycle's:</b> a reload that cannot read keeps whatever is cached - an
/// unreadable entry included - and tells nobody; a failed write keeps the edit cached and reports in
/// the lifecycle's words; and a missing body's reason is this store's own sentence rather than the
/// reader's exception message.
/// </para>
/// </remarks>
public sealed class CausalLoopDocumentStore : ICausalLoopDocumentStore
{
    private static readonly ILogger _logger = Log.ForContext<CausalLoopDocumentStore>();

    /// <summary>
    /// The lifecycle's own number of read attempts, for this module's tests that script a refusal
    /// and count the reads. Production uses the lifecycle's default, not this.
    /// </summary>
    internal const int DefaultReadAttempts = 5;

    /// <summary>The reason an entry gives when its body is not there.</summary>
    internal const string MissingReason = "the file is not there.";

    private readonly WritableDocumentLifecycle<CausalLoopDocumentEntry> _lifecycle;

    public CausalLoopDocumentStore()
    {
        _lifecycle = new WritableDocumentLifecycle<CausalLoopDocumentEntry>(Parsed, Serialize, Unavailable);
    }

    /// <summary>
    /// With the read and its retry supplied, so a test can refuse exactly as often as it chooses and
    /// wait for nothing - deterministic rather than patient.
    /// </summary>
    internal CausalLoopDocumentStore(Func<string, string> read, int readAttempts, TimeSpan betweenReadAttempts)
    {
        _lifecycle = new WritableDocumentLifecycle<CausalLoopDocumentEntry>(Parsed, Serialize, Unavailable, read, readAttempts, betweenReadAttempts);
    }

    /// <inheritdoc />
    public event EventHandler<CausalLoopDocumentChangedEventArgs>? Changed;

    /// <inheritdoc />
    public CausalLoopDocumentEntry GetOrLoad(string path) => _lifecycle.GetOrLoad(path);

    /// <inheritdoc />
    public DocumentSaveResult Save(string path, CausalLoopDocumentEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(entry);

        // THE ENTRY WRITTEN IS THE ONE THE CALLER EDITED, never one re-fetched from the cache.
        // Taking it as an argument is the enforcement: a save that looked the entry up itself wrote
        // the re-read file whenever a reload landed between a command's edit and its save, and
        // returned "" for success - the command's inverse then went onto the undo stack for a change
        // the file never received (fixed at 6c4f90d6).
        if (!entry.IsUsable)
        {
            // AN ENTRY THAT COULD NOT BE READ IS NOT A DOCUMENT TO WRITE. Its document is empty, and
            // writing it replaced a real diagram on disk with an empty file - measured: one reload
            // that could not read the body, then a save, and the .cld was gone. The file on disk is
            // the only copy left; leave it alone.
            _logger.Warning("Refusing to write {Path}: it could not be read ({Error})", path, entry.Error);
            return DocumentSaveResult.Failure($"This causal loop diagram could not be read, so it was not written. {entry.Error}");
        }

        // Re-parsed from the document being written, so the model and the bytes cannot disagree; the
        // lifecycle caches exactly this entry, whether or not the write lands.
        var parsed = CausalLoopParser.Parse(entry.Document);
        var result = _lifecycle.Save(path, entry with { Model = parsed.Model, Problems = parsed.Problems });
        return result;
    }

    /// <inheritdoc />
    public void Forget(string path) => _lifecycle.Forget(path);

    /// <inheritdoc />
    public void Reload(string path)
    {
        if (_lifecycle.Reload(path))
        {
            Changed?.Invoke(this, new CausalLoopDocumentChangedEventArgs(path));
        }
    }

    /// <inheritdoc />
    public void BodyDeleted(string path)
    {
        if (_lifecycle.BodyDeleted(path))
        {
            Changed?.Invoke(this, new CausalLoopDocumentChangedEventArgs(path));
        }
    }

    private static CausalLoopDocumentEntry Parsed(string path, string text)
    {
        _ = path;
        var document = CausalLoopDocument.Parse(text);
        var parsed = CausalLoopParser.Parse(document);
        return new CausalLoopDocumentEntry(document, parsed.Model, parsed.Problems, "");
    }

    private static string Serialize(CausalLoopDocumentEntry entry) => entry.Document.Text;

    /// <summary>
    /// A body that is missing or cannot be read opens as unreadable rather than as an empty diagram
    /// (R2.2): this module never invents a document where there is none, and <see cref="Save"/>
    /// refuses to write one.
    /// </summary>
    private static CausalLoopDocumentEntry Unavailable(string path, DocumentUnavailability unavailability, string reason)
    {
        _ = path;
        var because = unavailability == DocumentUnavailability.Missing || reason.Length == 0 ? MissingReason : reason;
        return CausalLoopDocumentEntry.Unreadable($"This causal loop diagram could not be read: {because}");
    }
}
