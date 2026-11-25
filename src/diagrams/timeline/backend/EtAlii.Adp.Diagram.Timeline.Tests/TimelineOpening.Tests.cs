using EtAlii.Adp.Diagram.Timeline.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Load-time validation of a timeline (FBL 17.4, etalii.adp spec 092, T016): a document id where it has none and each link
/// whose parts differ from what resolution finds are held pending on opening, never written then, written with the first
/// edit, and undone with it.
/// </summary>
public sealed class TimelineOpeningTests : IDisposable
{
    private const string Opened = "timeline: 1\nelements:\n  - id: f-signin\n    link: $k7q2m9xa@features.yaml#signin.Name=Sign-in\n    begin: 2027-01-04\n    row: 1\n  - id: beta\n    label: Beta\n    begin: 2027-04-05\n    row: 2\n";

    private readonly TestFolder _folder = TestFolder.Create("timeline-opening");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task OpeningATimeline_WritesNothing_AndHoldsItsDocumentIdAndItsRefreshedLinksPending()
    {
        // Arrange: the feature the link names was renamed in its own file since the link was written.
        var path = Write(Opened);
        var store = new TimelineDocumentStore(new FileWriter(), new TimelineOpeningIndex(Workspace("Log-in")));

        // Act.
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(Opened, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.NotNull(entry.Pending);
        Assert.Matches("^timeline: 1\nid: [0-9a-z]{8}\nelements:\n", entry.Pending);
        Assert.Contains("    link: $k7q2m9xa@features.yaml#signin.Name=Log-in\n", entry.Pending, StringComparison.Ordinal);
        Assert.DoesNotContain("label: Log-in", entry.Pending, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFirstEdit_WritesWhatOpeningFound_AndItsUndoTakesItBack()
    {
        // Arrange.
        var path = Write(Opened);
        var store = new TimelineDocumentStore(new FileWriter(), new TimelineOpeningIndex(Workspace("Log-in")));

        // Act.
        var result = await DocumentEditRun.RunAsync<TimelineDocumentStore, LineDocument, TimelineModel>(store, path, new TimelineOpeningEdit(), body => TimelineReading.Of(body.Text), Relabel, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);
        Assert.Matches("^timeline: 1\nid: [0-9a-z]{8}\nelements:\n", written);
        Assert.Contains("#signin.Name=Log-in\n", written, StringComparison.Ordinal);
        Assert.Contains("label: Beta 2\n", written, StringComparison.Ordinal);
        Assert.Equal(Opened, Assert.IsType<RestoreDocumentCommand<TimelineDocumentStore>>(result.Inverse).Text);
    }

    [Fact]
    public async Task ATimelineAsResolutionFindsIt_HoldsNothingPending()
    {
        // Arrange.
        var path = Write(Opened.Replace("timeline: 1\n", "timeline: 1\nid: r6d3w8ne\n", StringComparison.Ordinal));
        var store = new TimelineDocumentStore(new FileWriter(), new TimelineOpeningIndex(Workspace("Sign-in")));

        // Act.
        var entry = await store.GetOrLoadAsync(path, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Null(entry.Pending);
    }

    private string Write(string text)
    {
        var path = _folder.Combine("roadmap.tml");
        File.WriteAllText(path, text);
        return path;
    }

    private LinkWorkspace Workspace(string featureName) => new(_folder.Path.Replace('\\', '/'),
    [
        new IndexedDocument(_folder.Combine("features.yaml").Replace('\\', '/'), "k7q2m9xa", "Name", [new IndexedElement("signin", [new KeyValuePair<string, string>("Name", featureName)])], []),
    ]);

    private static EditOutcome Relabel(LineDocument body, TimelineModel model)
    {
        TimelineWriter.SetLabel(body, model.Elements.Single(element => element.Id == "beta"), "Beta 2");
        return EditOutcome.Applied;
    }
}

/// <summary>An edit of the tests' own, the redo of its undo.</summary>
internal sealed record TimelineOpeningEdit : ICommand;

/// <summary>An index that holds one workspace for every path.</summary>
internal sealed class TimelineOpeningIndex(LinkWorkspace workspace) : ILinkIndex
{
    public IDisposable Track(string rootPath) => throw new NotSupportedException();

    public LinkWorkspace WorkspaceOf(string path) => workspace;
}
