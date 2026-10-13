using EtAlii.Adp.Documents;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>The store over causal loop diagrams, held to the contract every module's is.</summary>
/// <remarks>
/// What is this module's own stays in the store's other test classes: a save after a reload that could
/// not read, a deleted body no longer drawn, and its own save never coming back as a reload.
/// </remarks>
public sealed class CausalLoopDocumentStoreTests : DocumentStoreContract<ICausalLoopDocumentStore, CausalLoopDocument, CausalLoopModel>
{
    internal static string OnCall => IoPath.Combine(ExamplesTests.ExamplesRoot, "on-call", "on-call.cld");

    protected override string Example => OnCall;

    protected override string EditMarker => "Store contract marker";

    protected override ICausalLoopDocumentStore CreateStore() => new CausalLoopDocumentStore();

    protected override void Edit(CausalLoopDocument document) =>
        CausalLoopWriter.AddVariable(document, CausalLoopParser.Parse(document).Model, "contractmarker", EditMarker);

    protected override string TextOf(CausalLoopDocument document) => document.Text;

    protected override bool IsEmpty(CausalLoopModel model) => model.Variables.Count == 0;
}
