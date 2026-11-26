using EtAlii.Adp.Files;
using EtAlii.Adp.Specification.Fbl.Links;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// A linked element of an open timeline shows its target as the workspace now holds it (DISL 4.12, FBL 17.1, etalii.adp spec
/// 092, T018): labelled by the target's current name, following its changes while the timeline is open, and marked broken,
/// labelled by the name it cached, while the link does not resolve.
/// </summary>
public sealed class TimelineLinkedLabelsTests : IDisposable
{
    private const string Link = "$k7q2m9xa@features.yaml#signin.Name=Sign-in";

    private readonly TestFolder _folder = TestFolder.Create("adp-timeline-linked-labels");
    private readonly TimelineDocumentStore _store = new(new FileWriter());
    private readonly TimelineElementMapper _mapper = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ALinkedElement_IsLabelledByItsTargetsCurrentName()
    {
        // Arrange: the target was renamed since the link cached its name.
        var path = Write();
        var index = new TimelineOpeningIndex(Workspace("Log in"));
        await using var session = new TimelineSession(path, _store, _mapper, links: index);

        // Act.
        var shown = Payload(await session.BaselineAsync(TestContext.Current.CancellationToken));

        // Assert.
        Assert.Equal("Log in", shown.Label);
        Assert.True(shown.Linked);
    }

    [Fact]
    public async Task ATargetRenamedWhileOpen_RelabelsTheElement()
    {
        // Arrange.
        var path = Write();
        var workspace = Workspace("Sign-in");
        var index = new TimelineOpeningIndex(workspace);
        await using var session = new TimelineSession(path, _store, _mapper, links: index);
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act.
        workspace.Put(Target("Log in"));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.NotNull(received);
        var shown = Payload(received);
        Assert.Equal("Log in", shown.Label);
        Assert.True(shown.Linked);
    }

    [Fact]
    public async Task ATargetDeletedWhileOpen_MarksTheElementBroken_UnderItsCachedName()
    {
        // Arrange.
        var path = Write();
        var workspace = Workspace("Log in");
        var index = new TimelineOpeningIndex(workspace);
        await using var session = new TimelineSession(path, _store, _mapper, links: index);
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        IReadOnlyList<DiagramDelta>? received = null;
        session.Changed += (_, args) => received = args.Deltas;

        // Act: the element is no longer in its document.
        workspace.Put(new IndexedDocument(Held("features.yaml"), "k7q2m9xa", "Name", [], []));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.NotNull(received);
        var shown = Payload(received);
        Assert.Equal("Sign-in", shown.Label);
        Assert.False(shown.Linked);
    }

    [Fact]
    public async Task AClosedTimeline_FollowsTheIndexNoLonger()
    {
        // Arrange.
        var path = Write();
        var workspace = Workspace("Sign-in");
        var index = new TimelineOpeningIndex(workspace);
        var session = new TimelineSession(path, _store, _mapper, links: index);
        await session.BaselineAsync(TestContext.Current.CancellationToken);
        var disturbed = false;
        session.Changed += (_, _) => disturbed = true;
        await session.DisposeAsync();

        // Act.
        workspace.Put(Target("Log in"));
        index.Raise(Held("features.yaml"));

        // Assert.
        Assert.False(disturbed);
    }

    private static TimelineElementPayload Payload(IReadOnlyList<DiagramDelta> deltas)
    {
        var add = Assert.IsType<DiagramAddDelta>(Assert.Single(deltas));
        return TimelineElementPayload.Parser.ParseFrom(add.Elements.Single(element => element.Id == "f-signin").Payload.Span);
    }

    private string Write()
    {
        var path = IoPath.Combine(_folder.Path, "roadmap.tml");
        File.WriteAllText(path, $"timeline: 1\nid: r6d3w8ne\nelements:\n  - id: f-signin\n    link: {Link}\n    begin: 2027-01-04\n    row: 1\n");
        _store.Forget(path);
        return path;
    }

    private string Held(string relative) => _folder.Path.Replace('\\', '/') + "/" + relative;

    private IndexedDocument Target(string name) =>
        new(Held("features.yaml"), "k7q2m9xa", "Name", [new IndexedElement("signin", [new KeyValuePair<string, string>("Name", name)])], []);

    private LinkWorkspace Workspace(string name) => new(_folder.Path.Replace('\\', '/'), [Target(name)]);
}
