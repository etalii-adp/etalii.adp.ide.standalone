using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The <c>.fdg</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
/// <remarks>
/// <b>There is no parse error.</b> <see cref="FdgParser"/> never throws: a malformed entry is passed
/// over and reported in <see cref="FdgModel.Problems"/>, so a document that reads always has a model.
/// </remarks>
public sealed class FdgDocumentStore : LifecycleDocumentStore<LineDocument, FdgModel>, IFdgDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<FdgModel> Parse(LineDocument body) => FdgParser.Outcome(body);
}
