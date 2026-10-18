using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <inheritdoc cref="ICausalLoopDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a causal loop diagram reads (code-structure FR-029).</b> Opening, the
/// retries before a refused read is believed, keeping the last good diagram through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save (R2.3),
/// refusing to write a diagram that could not be read and telling the sessions are all
/// <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s. What stays here is what is this module's: how a
/// body's text is opened through the binding, which every edit writes by splices, and what it states, which
/// is the binding's reading (<see cref="CausalLoopBody"/>, causal-loop-disl-fbl Requirements 4 and 5).
/// </para>
/// <para>
/// <b>Every text reads.</b> The binding's last rule takes a line no other rule reads rather than refusing
/// the file, and the validator reports those lines, so no body of this type is unreadable for what it
/// says - only for a read that failed.
/// </para>
/// </remarks>
public sealed class CausalLoopDocumentStore(IFileWriter writer) : LifecycleDocumentStore<CausalLoopBody, CausalLoopModel>(writer), ICausalLoopDocumentStore
{
    /// <inheritdoc />
    protected override CausalLoopBody Read(string text) => CausalLoopBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(CausalLoopBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<CausalLoopModel> Parse(CausalLoopBody body) => new(body.Read(), []);
}
