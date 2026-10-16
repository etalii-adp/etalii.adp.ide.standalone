using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The <c>.ghg</c> document store: the shared store, told how a body reads through its binding and
/// what it means.
/// </summary>
public sealed class GhgDocumentStore : LifecycleDocumentStore<GhgBody, GhgModel>, IGhgDocumentStore
{
    /// <inheritdoc />
    protected override GhgBody Read(string text) => GhgBody.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(GhgBody body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<GhgModel> Parse(GhgBody body) => GhgParser.Outcome(body);
}
