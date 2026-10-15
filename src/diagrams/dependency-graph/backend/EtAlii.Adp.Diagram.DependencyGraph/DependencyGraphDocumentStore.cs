using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <inheritdoc cref="IDependencyGraphDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a graph reads (code-structure FR-029).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save
/// (R2.3) and telling the sessions are all <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s.
/// What stays here is what is this module's: what a body's lines mean, read through the parser's
/// <see cref="ParseOutcome{TModel}"/>, which says why a body that is not YAML cannot be read.
/// </para>
/// <para>
/// <b>A file that is not YAML is an ordinary state for a file somebody is editing</b>, so it is held
/// as unreadable with the parser's sentence, rather than thrown out of the store, and nothing is
/// written to it until it parses.
/// </para>
/// </remarks>
public sealed class DependencyGraphDocumentStore : LifecycleDocumentStore<LineDocument, DependencyGraphModel>, IDependencyGraphDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    protected override DependencyGraphModel Rebuild(LineDocument body) => DependencyGraphParser.Outcome(body).Model;

    /// <inheritdoc />
    protected override ParseOutcome<DependencyGraphModel> Parse(LineDocument body) => DependencyGraphParser.Outcome(body);
}
