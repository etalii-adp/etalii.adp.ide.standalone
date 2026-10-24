using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The <c>.skv</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
/// <remarks>
/// A body is read through the binding (<see cref="SankeyReader"/>); one that is not YAML is held as unreadable with
/// the YAML reader's sentence, so that nothing is written to it until it parses (Q3).
/// </remarks>
public sealed class SankeyDocumentStore(IFileWriter writer) : LifecycleDocumentStore<LineDocument, SankeyModel>(writer), ISankeyDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<SankeyModel> Parse(LineDocument body) => SankeyReader.Outcome(body.Text);
}
