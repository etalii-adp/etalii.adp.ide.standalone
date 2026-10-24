using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Xunit;
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// A .NET project node, selected through the real host, answers the property grid - whether the tab
/// was opened at the solution or at its <c>.adp</c> registration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> <c>DotNetContextPropertyProvider</c> reads the file its target names as a
/// solution, so it is correct only if an element's <c>ContextTarget.ResolvedFullPath</c> is the
/// solution BODY. <c>DotNetContextSourceResolver</c> builds that path from the router's body, which
/// until now was established by reading the code: <see cref="DotNetDependencyGraphFlowTests"/> never
/// asks for properties, and the provider's unit guard proves its own rule, not the wiring that feeds
/// it.
/// </para>
/// <para>
/// <b>Both tabs, because only one of them can fail.</b> A tab opened at the <c>.slnx</c> hands the
/// resolver the solution as its parent, so "the parent's path" and "the routed body" are the same
/// file and a wrong base passes. A tab opened at the <c>.adp</c> hands it the registration - the case
/// centralized-selection task 26 found loading the registration as a solution and resolving nothing.
/// </para>
/// </remarks>
public class DotNetDependencyGraphPropertiesFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string CoreProjectId = "project:src/Pipeline.Core/Pipeline.Core.csproj";

    private readonly string _projectFolder = ShowcaseFolder();

    /// <summary>
    /// The shipped showcase, read only: nothing here writes, because a .NET dependency graph changes
    /// nothing in the solution it shows.
    /// </summary>
    private static string ShowcaseFolder() =>
        RepositoryPaths.Under("src", "examples", "diagrams", "dotnet-dependency-graph", "pipeline-toolkit");

    [Theory]
    [InlineData("PipelineToolkit.slnx")]
    [InlineData("PipelineToolkit.adp")]
    public async Task AProjectNode_SelectedThroughTheHost_AnswersThePropertyGrid(string openedAt)
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var watchId = ShortGuid.NewShortGuid();
        var hierarchy = session.Hierarchy;
        var context = session.Context;
        var entryId = await NestedEntryLookup.EntryIdOfAsync(hierarchy, session.ProjectId, watchId, session.Headers, openedAt);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watch = context.Watch(
            new WatchContextRequest { ProjectId = session.ProjectId, WatchId = watchId },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        await StreamProbe.WaitUntilContextLiveAsync(watch.ResponseStream, cts.Token);
        var pendingSelection = ReadUntilElementSelectedAsync(watch.ResponseStream, cts.Token);

        // Act: the project node, selected under the file the tab was opened at.
        var selected = await context.SelectAsync(
            new SelectRequest { ProjectId = session.ProjectId, WatchId = watchId, Selection = ElementChain(entryId, CoreProjectId) },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the selection resolved to the project...
        Assert.Equal("", selected.Error);
        var changed = await pendingSelection;
        Assert.Equal("Pipeline.Core", changed.Levels[^1].Element.Text);

        // ...and the property grid, asking the provider with the target the resolver built, reads
        // the project out of the SOLUTION - which it can only do if that target is the solution body.
        var described = await context.DescribePropertiesAsync(
            new DescribePropertiesRequest { ProjectId = session.ProjectId, WatchId = watchId },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        var rows = described.Properties.ToDictionary(property => property.Id, property => property.Value);
        Assert.True(rows.Count > 0, $"Opened at {openedAt}, the project node answered no property rows.");
        Assert.Equal("Pipeline.Core", rows["dotnet.name"]);
        Assert.Equal("src/Pipeline.Core/Pipeline.Core.csproj", rows["dotnet.path"]);
        Assert.Equal("net10.0", rows["dotnet.target-frameworks"]);
    }

    private static ContextSelection ElementChain(ShortGuid entryId, string elementId) =>
        new()
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
                None = new Empty(),
            },
        };

    /// <summary>The first pushed selection that reaches the element level - the file level alone is not the answer.</summary>
    private static async Task<ContextSelectionChanged> ReadUntilElementSelectedAsync(
        IAsyncStreamReader<ContextMessage> stream,
        CancellationToken cancellationToken)
    {
        while (await stream.MoveNext(cancellationToken))
        {
            if (stream.Current.MessageCase == ContextMessage.MessageOneofCase.Selection &&
                stream.Current.Selection.Selection is not null &&
                stream.Current.Selection.Levels.Count >= 2)
            {
                return stream.Current.Selection;
            }
        }

        throw new InvalidOperationException("The stream ended before an element selection arrived.");
    }
}
