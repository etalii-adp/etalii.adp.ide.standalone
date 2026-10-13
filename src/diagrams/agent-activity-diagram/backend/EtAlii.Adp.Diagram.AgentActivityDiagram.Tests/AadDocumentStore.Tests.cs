using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>The store over activity files, held to the contract every module's is, and what is its own.</summary>
public sealed class AadDocumentStoreTests : DocumentStoreContract<IAadDocumentStore, AadBody, AadModel>
{
    internal static string OneDay => Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-day.aad");

    protected override string Example => OneDay;

    protected override string EditMarker => "Store contract marker";

    protected override IAadDocumentStore CreateStore() => new AadDocumentStore();

    protected override void Edit(AadBody document) =>
        document.Change(new ModelChange.Set(AadModel.Read(document).Elements[0].Id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = EditMarker }));

    protected override string TextOf(AadBody document) => document.Text;

    protected override bool IsEmpty(AadModel model) => model.Elements.Count == 0;

    [Fact]
    public void AFileThatIsNotAnActivityFile_IsHeldUnreadable_AndAnEditIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("aad-store");
        var body = folder.Combine("broken.aad");
        File.WriteAllText(body, "projects: [unterminated\r\n");
        var store = new AadDocumentStore();

        // Act.
        var entry = store.GetOrLoad(body);
        var saved = store.Save(body, entry, _ => "");

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.True(saved.Result.Failed);
        Assert.Equal("projects: [unterminated\r\n", File.ReadAllText(body));
    }

    [Fact]
    public void AnEdit_IsAppliedToWhatTheFileHoldsNow_WhenItWasWrittenSinceItWasRead()
    {
        // Arrange.
        using var folder = TestFolder.Create("aad-store");
        var body = folder.Combine("work.aad");
        File.Copy(Example, body);
        var store = new AadDocumentStore();
        var basis = store.GetOrLoad(body);
        var written = File.ReadAllText(body) + "# an agent's line\r\n";
        File.WriteAllText(body, written);

        // Act.
        var saved = store.Save(body, basis, document =>
            document.Change(new ModelChange.Set(AadModel.Read(document).Elements[0].Id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = EditMarker })).Refusal ?? "");

        // Assert.
        Assert.False(saved.Result.Failed, saved.Result.Error);
        Assert.Equal(written, saved.Before);
        Assert.Contains("# an agent's line", File.ReadAllText(body), StringComparison.Ordinal);
        Assert.Contains(EditMarker, File.ReadAllText(body), StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedEdit_WritesNothing_AndTellsNoSession()
    {
        // Arrange.
        using var folder = TestFolder.Create("aad-store");
        var body = folder.Combine("work.aad");
        File.Copy(Example, body);
        var before = File.ReadAllText(body);
        var store = new AadDocumentStore();
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act.
        var saved = store.Save(body, store.GetOrLoad(body), _ => "Not today.");

        // Assert.
        Assert.Equal("Not today.", saved.Result.Error);
        Assert.Equal(before, saved.After);
        Assert.Equal(before, File.ReadAllText(body));
        Assert.Equal(0, told);
    }
}
