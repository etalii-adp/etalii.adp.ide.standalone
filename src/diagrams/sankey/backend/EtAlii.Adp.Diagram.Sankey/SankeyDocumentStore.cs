using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The <c>.skv</c> document store: the shared store, told how a body reads and what it means.
/// </summary>
/// <remarks>
/// A body is held as a <see cref="SankeyBody"/>, read through the binding and written by its changes; one that is not
/// YAML is held as unreadable with the YAML reader's sentence, so that nothing is written to it until it parses (Q3).
/// </remarks>
public sealed class SankeyDocumentStore(IFileWriter writer) : LifecycleDocumentStore<SankeyBody, SankeyModel>(writer), ISankeyDocumentStore
{
    /// <inheritdoc />
    protected override SankeyBody Read(string text) => SankeyBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(SankeyBody body) => body.Text;

    /// <inheritdoc />
    protected override ParseOutcome<SankeyModel> Parse(SankeyBody body) => body.Reading;
}
