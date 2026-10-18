using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>The store over C4 models, held to the contract every module's is.</summary>
/// <remarks>
/// What is this module's own stays in the store's other test classes: one document shared by every
/// view, a save that changes only the edited line, a save after a reload that could not read, and a
/// deleted body no longer drawn.
/// </remarks>
public sealed class C4DocumentStoreTests : DocumentStoreContract<IC4DocumentStore, LineDocument, C4Workspace>
{
    internal static string Courier => IoPath.Combine(RepositoryPaths.Under("src", "diagrams", "c4", "examples"), "reference", "architecture", "courier.dsl");

    protected override string Example => Courier;

    protected override string EditMarker => "Store contract marker";

    protected override IC4DocumentStore CreateStore() => new C4DocumentStore(new FileWriter());

    protected override void Edit(LineDocument document)
    {
        var line = document.CodeLines().First(l => l.Code.Contains("softwareSystem", StringComparison.Ordinal));
        document.ReplaceLine(line.Number, line.Text.Replace("softwareSystem \"", "softwareSystem \"" + EditMarker + " ", StringComparison.Ordinal));
    }

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(C4Workspace model) => model.Elements.Count == 0;
}
