using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <inheritdoc cref="IRdfDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how Turtle reads (code-structure FR-029).</b> Opening, the retries
/// before a refused read is believed, keeping the last good document through a reload that cannot
/// read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own save
/// (R2.3), refusing to write over a document that does not parse (Requirement 1.5) and telling the
/// sessions are all <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s. What stays here is what
/// is this module's: what a body's lines state, and why a body that is not Turtle cannot be read.
/// </para>
/// <para>
/// <b>A file that is not Turtle is an ordinary state for a file somebody is editing</b>, so it is
/// held as unreadable with the parser's sentence and the line it stopped at, rather than thrown out
/// of the store.
/// </para>
/// </remarks>
public sealed class RdfDocumentStore : LifecycleDocumentStore<LineDocument, RdfModel>, IRdfDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    protected override RdfModel Rebuild(LineDocument body) => RdfParser.Outcome(body).Model;

    /// <inheritdoc />
    protected override ParseOutcome<RdfModel> Parse(LineDocument body) => RdfParser.Outcome(body);
}
