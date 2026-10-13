using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <inheritdoc cref="ICausalLoopDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a causal loop diagram reads (code-structure FR-029).</b> Opening, the
/// retries before a refused read is believed, keeping the last good diagram through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save (R2.3),
/// refusing to write a diagram that could not be read and telling the sessions are all
/// <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s. What stays here is what is this module's: how a
/// body's text becomes lines, and what those lines state.
/// </para>
/// <para>
/// <b>Every text reads.</b> The grammar passes over a line it cannot read rather than refusing the file,
/// and the validator reports those lines from its own parse, so no body of this type is unreadable for
/// what it says - only for a read that failed.
/// </para>
/// </remarks>
public sealed class CausalLoopDocumentStore : LifecycleDocumentStore<CausalLoopDocument, CausalLoopModel>, ICausalLoopDocumentStore
{
    /// <inheritdoc />
    protected override CausalLoopDocument Read(string text) => CausalLoopDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(CausalLoopDocument body) => body.Text;

    /// <inheritdoc />
    protected override CausalLoopModel Rebuild(CausalLoopDocument body) => CausalLoopParser.Parse(body).Model;
}
