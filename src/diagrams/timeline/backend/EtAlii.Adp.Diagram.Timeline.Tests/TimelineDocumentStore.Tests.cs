using EtAlii.Adp.Diagram.Timeline.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>The store over timelines, held to the contract every module's is, and what is its own.</summary>
public sealed class TimelineDocumentStoreTests : DocumentStoreContract<ITimelineDocumentStore, LineDocument, TimelineModel>
{
    internal static string Simple => Path.Combine(AppContext.BaseDirectory, "Fixtures", "simple.tml");

    protected override string Example => Simple;

    protected override string EditMarker => "Store contract marker";

    protected override ITimelineDocumentStore CreateStore() => new TimelineDocumentStore(new FileWriter());

    protected override void Edit(LineDocument document) =>
        TimelineWriter.SetLabel(document, TimelineReading.Of(document.Text).Elements[0], EditMarker);

    protected override string TextOf(LineDocument document) => document.Text;

    protected override bool IsEmpty(TimelineModel model) => model.Elements.Count == 0;

    [Fact]
    public async Task ATimelineThatIsNotYaml_IsHeldUnreadable_AndASaveIsRefusedUnwritten()
    {
        // Arrange.
        using var folder = TestFolder.Create("timeline-store");
        var body = folder.Combine("broken.tml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "findings", "unparseable.tml"), body);
        var before = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
        var store = new TimelineDocumentStore(new FileWriter());

        // Act.
        var entry = store.GetOrLoad(body);
        var saved = await store.SaveAsync(body, entry.Document, TestContext.Current.CancellationToken);

        // Assert: what the file holds is unknown, so the empty model is not written over it.
        Assert.False(entry.IsUsable);
        Assert.Empty(entry.Model.Elements);
        Assert.True(saved.Failed);
        Assert.Equal(before, await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ATimelineThatIsNotYaml_IsOneProblemAtTheLineTheReaderStopped()
    {
        // Arrange.
        using var folder = TestFolder.Create("timeline-store");
        var body = folder.Combine("broken.tml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "findings", "unparseable.tml"), body);
        var store = new TimelineDocumentStore(new FileWriter());

        // Act.
        var entry = store.GetOrLoad(body);

        // Assert: the reader stops at the fourth line, `label: one: two`; a problem's line is zero-based.
        var problem = Assert.Single(entry.Problems);
        Assert.Equal(3, problem.Line);
        Assert.StartsWith("it does not parse at line 4. ", entry.Unreadable, StringComparison.Ordinal);
        Assert.EndsWith(problem.Message, entry.Unreadable, StringComparison.Ordinal);
    }
}
