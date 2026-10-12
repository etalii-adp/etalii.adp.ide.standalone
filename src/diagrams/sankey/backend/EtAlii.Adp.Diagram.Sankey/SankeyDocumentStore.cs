using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The <c>.skv</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
public sealed class SankeyDocumentStore : LifecycleDocumentStore<LineDocument, SankeyModel>, ISankeyDocumentStore
{
    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;

    /// <inheritdoc />
    protected override SankeyModel Rebuild(LineDocument body) => SankeyParser.Parse(body);
}
