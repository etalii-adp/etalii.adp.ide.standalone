using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

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
public sealed class RdfDocumentStore(IFileWriter writer) : LifecycleDocumentStore<RdfBody, RdfModel>(writer), IRdfDocumentStore
{
    /// <inheritdoc />
    protected override RdfBody Read(string text) => RdfBody.Parse(text);

    /// <inheritdoc />
    /// <remarks>The body's name tells its plugin the body's syntax, the stand-in for gap 5.</remarks>
    protected override RdfBody Locate(string path, RdfBody body) => body.At(path);

    /// <inheritdoc />
    protected override string TextOf(RdfBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<RdfModel> Parse(RdfBody body) => RdfParser.Outcome(body.Lines);
}
