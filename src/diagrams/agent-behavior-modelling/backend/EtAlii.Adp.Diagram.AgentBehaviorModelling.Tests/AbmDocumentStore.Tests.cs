namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>The store over Markdown bodies, held to the contract every module's is.</summary>
public sealed class AbmDocumentStoreTests : DocumentStoreContract<IAbmDocumentStore, AbmBody, AbmModel>
{
    protected override string Example => AbmExamples.BodyOf("bug-fixer");

    protected override string EditMarker => "Store contract marker";

    protected override IAbmDocumentStore CreateStore() => new AbmDocumentStore();

    protected override void Edit(AbmBody document) =>
        document.Change(AbmEdits.Set(document.Model.Nodes[0], "label", EditMarker));

    protected override string TextOf(AbmBody document) => document.Text;

    protected override bool IsEmpty(AbmModel model) => model.Nodes.Count == 0;
}
