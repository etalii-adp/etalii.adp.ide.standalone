using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>The store over Wardley maps, held to the contract every module's is.</summary>
/// <remarks>
/// What is this module's own stays in the store's other test classes: one document shared by every
/// connection, the bytes a save keeps, the sibling a first save creates, and a document an editor is
/// still writing. The identities held beside a document are held to their own facts below.
/// </remarks>
public sealed class WardleyDocumentStoreTests : DocumentStoreContract<IWardleyDocumentStore, LineDocument, WardleyMap>
{
    internal static string Minimal => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "minimal.owm");

    protected override string Example => Minimal;

    protected override string EditMarker => "Store contract marker";

    protected override IWardleyDocumentStore CreateStore() => new WardleyDocumentStore(new FileWriter());

    protected override void Edit(LineDocument document) => WardleyWriter.ReplaceLine(document, 1, "title " + EditMarker);

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(WardleyMap model) => model.Components.Count == 0;

    [Fact]
    public async Task AnEntry_CarriesTheLinesItsParserPassedOver()
    {
        // Act.
        var entry = await new WardleyDocumentStore(new FileWriter()).GetOrLoadAsync(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", "unmodelled-statements.owm"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(entry.IsUsable);
        Assert.Equal(13, Assert.Single(entry.Problems).Line);
    }
}
