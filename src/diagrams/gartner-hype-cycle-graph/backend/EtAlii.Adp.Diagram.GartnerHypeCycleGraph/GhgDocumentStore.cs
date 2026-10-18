using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The <c>.ghg</c> document store: the shared store, told how a body reads through its binding and
/// what it means.
/// </summary>
public sealed class GhgDocumentStore(IFileWriter writer) : LifecycleDocumentStore<GhgBody, GhgModel>(writer), IGhgDocumentStore
{
    /// <inheritdoc />
    protected override GhgBody Read(string text) => GhgBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(GhgBody body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<GhgModel> Parse(GhgBody body) => GhgParser.Outcome(body);
}
