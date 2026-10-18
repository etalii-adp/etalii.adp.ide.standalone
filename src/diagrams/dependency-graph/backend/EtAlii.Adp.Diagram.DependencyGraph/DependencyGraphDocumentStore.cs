using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <inheritdoc cref="IDependencyGraphDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a graph reads (code-structure FR-029).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save
/// (R2.3) and telling the sessions are all <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s.
/// What stays here is what is this module's: a body read through the module's binding, and its DISL
/// model, with the sentence that says why a body that is not YAML cannot be read.
/// </para>
/// <para>
/// <b>A file that is not YAML is an ordinary state for a file somebody is editing</b>, so it is held
/// as unreadable with YAML's own sentence, rather than thrown out of the store, and nothing is
/// written to it until it reads.
/// </para>
/// </remarks>
public sealed class DependencyGraphDocumentStore(IFileWriter writer) : LifecycleDocumentStore<DependencyGraphBody, DislModel>(writer), IDependencyGraphDocumentStore
{
    /// <inheritdoc />
    protected override DependencyGraphBody Read(string text) => DependencyGraphBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(DependencyGraphBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<DislModel> Parse(DependencyGraphBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (!body.IsUnreadable) return new ParseOutcome<DislModel>(body.Disl, []);

        (string reason, int line) = DependencyGraphFindings.UnreadableReason(body);
        return new ParseOutcome<DislModel>(body.Disl, [new ParseProblem(line - 1, reason)], reason);
    }
}
