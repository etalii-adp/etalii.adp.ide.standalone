using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>The store over activity files, held to the contract every module's is, and what is its own.</summary>
public sealed class AadDocumentStoreTests : DocumentStoreContract<IAadDocumentStore, AadBody, AadModel>
{
    internal static string OneDay => Path.Combine(AppContext.BaseDirectory, "Fixtures", "one-day.aad");

    protected override string Example => OneDay;

    protected override string EditMarker => "Store contract marker";

    protected override IAadDocumentStore CreateStore() => new AadDocumentStore(new FileWriter());

    protected override void Edit(AadBody document) =>
        document.Change(new ModelChange.Set(AadModel.Read(document).Elements[0].Id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = EditMarker }));

    protected override string TextOf(AadBody document) => document.Text;

    protected override bool IsEmpty(AadModel model) => model.Elements.Count == 0;

    [Fact]
    public async Task AFileThatIsNotAnActivityFile_IsHeldUnreadable_AndAnEditIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("aad-store");
        var body = folder.Combine("broken.aad");
        await File.WriteAllTextAsync(body, "projects: [unterminated\r\n", TestContext.Current.CancellationToken);
        var store = new AadDocumentStore(new FileWriter());

        // Act.
        var entry = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        var saved = await store.SaveAsync(body, entry, _ => "", TestContext.Current.CancellationToken);

        // Assert.
        Assert.False(entry.IsUsable);
        Assert.True(saved.Result.Failed);
        Assert.Equal("projects: [unterminated\r\n", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEdit_IsAppliedToWhatTheFileHoldsNow_WhenItWasWrittenSinceItWasRead()
    {
        // Arrange.
        using var folder = TestFolder.Create("aad-store");
        var body = folder.Combine("work.aad");
        File.Copy(Example, body);
        var store = new AadDocumentStore(new FileWriter());
        var basis = await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken);
        var written = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken) + "# an agent's line\r\n";
        await File.WriteAllTextAsync(body, written, TestContext.Current.CancellationToken);

        // Act.
        var saved = (await store.SaveAsync(body, basis, document =>
            document.Change(new ModelChange.Set(AadModel.Read(document).Elements[0].Id, new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = EditMarker })).Refusal ?? "", TestContext.Current.CancellationToken));

        // Assert.
        Assert.False(saved.Result.Failed, saved.Result.Error);
        Assert.Equal(written, saved.Before);
        Assert.Contains("# an agent's line", await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Contains(EditMarker, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARefusedEdit_WritesNothing_AndTellsNoSession()
    {
        // Arrange.
        using var folder = TestFolder.Create("aad-store");
        var body = folder.Combine("work.aad");
        File.Copy(Example, body);
        var before = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        var store = new AadDocumentStore(new FileWriter());
        var told = 0;
        store.Changed += (_, _) => told++;

        // Act.
        var saved = await store.SaveAsync(body, await store.GetOrLoadAsync(body, TestContext.Current.CancellationToken), _ => "Not today.", TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("Not today.", saved.Result.Error);
        Assert.Equal(before, saved.After);
        Assert.Equal(before, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
        Assert.Equal(0, told);
    }
}
