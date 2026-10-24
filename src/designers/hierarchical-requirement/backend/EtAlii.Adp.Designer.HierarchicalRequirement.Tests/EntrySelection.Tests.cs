using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Xunit;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// An entry of a requirement document as the page names it over the wire (spec 029 T074, FR-012): selected as the
/// file's child, and the target of a key the page sends with the file's entry beside it. Found missing by the browser
/// check of T081 - the actions answered for an entry, but no resolver turned the page's element id into one, so the
/// host refused every key and every selection of an entry as "an unrecognised source".
/// </summary>
public sealed class EntrySelectionTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder("requirement-selection");

    private string Seed()
    {
        var body = IoPath.Combine(_projectFolder, "web-shop.md");
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"), body);
        return body;
    }

    [Fact]
    public async Task AnEntry_IsSelectedAsItsFilesChild()
    {
        // Arrange.
        Seed();
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var entryId = await NestedEntryLookup.EntryIdOfAsync(session.Hierarchy, session.ProjectId, session.WatchId, session.Headers, "web-shop.md");

        // Act: what the page sends when a box is clicked.
        var selected = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = EntryChain(entryId, "REQ-0005") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var gone = await session.Context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Selection = EntryChain(entryId, "REQ-9999") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the entry is accepted, and an entry the file does not hold is not.
        Assert.Equal("", selected.Error);
        Assert.NotEqual("", gone.Error);
    }

    [Fact]
    public async Task CtrlEnter_OnAnEntryTheFileHolds_AddsBelowIt()
    {
        // Arrange.
        var body = Seed();
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var entryId = await NestedEntryLookup.EntryIdOfAsync(session.Hierarchy, session.ProjectId, session.WatchId, session.Headers, "web-shop.md");

        // Act: the page's key, named by the entry and the file it is in, with nothing selected first.
        var executed = await session.Context.ExecuteActionAsync(
            new ExecuteActionRequest
            {
                ProjectId = session.ProjectId,
                WatchId = session.WatchId,
                Source = new ContextSource { ElementId = new ElementId { Value = "REQ-0009" }, DiagramEntryId = entryId },
                InteractionId = ShortGuid.NewShortGuid(),
                Shortcut = new ContextShortcut { Key = "Enter", Ctrl = true },
            },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: accepted, and the new requirement stands directly below REQ-0009.
        Assert.True(executed.Accepted, executed.Error);
        var lines = (await File.ReadAllLinesAsync(body, TestContext.Current.CancellationToken)).ToList();
        var after = lines[lines.FindIndex(line => line.Contains("REQ-0009 ", StringComparison.Ordinal)) + 1];
        Assert.Contains("New requirement", after, StringComparison.Ordinal);
    }

    private static ContextSelection EntryChain(ShortGuid entryId, string id) => new()
    {
        Source = ContextSelectionSource.Explorer,
        Id = new ContextSource { EntryId = entryId },
        Path = new Path(),
        Child = new ContextSelection
        {
            Source = ContextSelectionSource.DiagramCanvas,
            Id = new ContextSource { ElementId = new ElementId { Value = id } },
            Path = new Path(),
        },
    };
}
