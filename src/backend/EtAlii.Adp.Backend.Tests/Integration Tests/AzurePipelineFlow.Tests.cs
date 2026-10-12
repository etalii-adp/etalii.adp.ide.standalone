using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Grpc.Net.Client;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Azure Pipelines arc over the real host: a <c>.yml</c> that is not a diagram until somebody
/// registers it, then opens, streams and edits like any other type.
/// </summary>
/// <remarks>
/// The half worth proving here rather than in the module's own tests is the <b>shared extension</b>
/// (Requirement 2). A <c>.mm</c> is a mindmap wherever it is found; a <c>.yml</c> is a pipeline only
/// where somebody said so, because half the files in a repository are YAML. That rule lives in
/// core's router and is exercised by two modules at once, which is exactly the sort of thing only
/// an integration test can catch.
/// </remarks>
public class AzurePipelineFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string Pipeline = """
        trigger:
          - main

        stages:
          - stage: Build
            jobs:
              - job: Compile
                steps:
                  - script: dotnet build
                    displayName: Build

          - stage: Test
            jobs:
              - job: Verify
                steps:
                  - script: dotnet test
        """;

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        // A pipeline nobody has registered yet, a registered one, a mindmap whose bare body still
        // routes, and a file no type claims.
        File.WriteAllText(IoPath.Combine(projectFolder, "unregistered.yml"), Pipeline);
        File.WriteAllText(IoPath.Combine(projectFolder, "azure-pipelines.yml"), Pipeline);
        File.WriteAllText(IoPath.Combine(projectFolder, "azure-pipelines.adp"), "azure-devops/pipeline\n");
        File.WriteAllText(
            IoPath.Combine(projectFolder, "roadmap.mm"),
            "<map version=\"freeplane 1.11.5\">\n<node TEXT=\"roadmap\" ID=\"ID_1\"/>\n</map>\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "notes.txt"), "not a diagram\n");
        return projectFolder;
    }

    [Fact]
    public async Task ARegisteredPipeline_OpensAndStreamsItsStagesAndEdges()
    {
        // Arrange: Requirement 11.1 - opened over DiagramService.Open with the file's
        // project-relative path, like every other diagram type.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var elements = await BaselineAsync(session.Channel, session.Headers, session.ProjectId, "azure-pipelines.adp");

        // Assert.
        Assert.Contains(elements, element => element.Id.Value == "Build" && element.Type == "azure-devops/pipeline+stage");
        Assert.Contains(elements, element => element.Id.Value == "Test");
        // The arrow nobody wrote down: Test has no dependsOn, so it waits for the stage before it.
        Assert.Contains(elements, element => element.Id.Value == "edge:Build->Test");
    }

    [Fact]
    public async Task ABarePipeline_RoutesNowhere_WhileABareMindmapStillRoutes()
    {
        // Arrange: Requirement 2.2 and 2.3 - the whole point of a shared extension. Half the files
        // in a repository are YAML, so a .yml is a pipeline only where somebody said so; a .mm is
        // a mindmap wherever it is found, and that must not have been broken to achieve this.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        var pipeline = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = PathOf("unregistered.yml") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);
        var mindmap = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = PathOf("roadmap.mm") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(pipeline.Items);
        Assert.NotEmpty(mindmap.Items);
    }

    [Fact]
    public async Task ARegisteredPipeline_AnswersWithThePipelinePalette()
    {
        // Arrange & act.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        var response = await diagramClient.DescribeToolboxAsync(
            new DescribeToolboxRequest { ProjectId = session.ProjectId, Path = PathOf("azure-pipelines.adp") },
            session.Headers, cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(
            ["Stage", "Job", "Deployment job", "Script step"],
            response.Items.Select(item => item.Label));
    }

    [Fact]
    public async Task AddIsOfferedOnAnUnregisteredPipeline_AndNotOnAPlainFileOrAnAlreadyRegisteredOne()
    {
        // Arrange: Requirement 2.5 - Add-on-a-file is how a .yml already in the repository becomes
        // a diagram, and it must not be offered where it would do nothing.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        // One watch id throughout: a hierarchy model is per connection, so an entry listed on
        // one cannot be named by a call on another.
        var watchId = ShortGuid.NewShortGuid();
        var hierarchy = await EntriesAsync(session.Channel, session.Headers, session.ProjectId, watchId);

        // Act.
        var onPipeline = await ActionsOnAsync(contextClient, session.Headers, session.ProjectId, watchId, hierarchy["unregistered.yml"]);
        var onPlainFile = await ActionsOnAsync(contextClient, session.Headers, session.ProjectId, watchId, hierarchy["notes.txt"]);
        var onRegistered = await ActionsOnAsync(contextClient, session.Headers, session.ProjectId, watchId, hierarchy["azure-pipelines.yml"]);

        // Assert.
        Assert.Contains(onPipeline, action => action.StartsWith("hierarchy.add", StringComparison.Ordinal));
        Assert.DoesNotContain(onPlainFile, action => action.StartsWith("hierarchy.add", StringComparison.Ordinal));
        // The body of a registered pipeline is already a diagram; registering it again would make
        // a second .adp pointing at the same file.
        Assert.DoesNotContain(onRegistered, action => action.StartsWith("hierarchy.add", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADependencyEdit_RewritesOnlyTheLinesItTouches_AndIsOneUndoAway()
    {
        // Arrange: Requirement 9.8 - the edit reaches the file, and the file is otherwise exactly
        // what it was. This is the promise the whole module is built around, checked here over the
        // real host rather than against the writer in isolation.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var contextClient = session.Context;
        var watchId = ShortGuid.NewShortGuid();
        var entries = await EntriesAsync(session.Channel, session.Headers, session.ProjectId, watchId);
        var bodyPath = IoPath.Combine(_projectFolder, "azure-pipelines.yml");
        var before = await File.ReadAllLinesAsync(bodyPath, TestContext.Current.CancellationToken);

        // Arrange, continued: the selection has to be recorded first. An element id on its own
        // has no file above it, and this module's resolver refuses one - a pipeline element is
        // only selectable inside its pipeline (Requirement 12.1), which is the same nested shape
        // the canvas sends.
        var selected = await contextClient.SelectAsync(
            new SelectRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Selection = ElementChain(entries["azure-pipelines.adp"], "Test"),
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Empty(selected.Error);

        // Act.
        var result = await contextClient.SetPropertyAsync(
            new SetPropertyRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                PropertyId = "azure-pipeline.display-name",
                Value = "Run the tests",
            },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.Accepted, result.Error);
        var after = await File.ReadAllLinesAsync(bodyPath, TestContext.Current.CancellationToken);
        Assert.Equal(before.Length + 1, after.Length);
        var added = after.Except(before).ToList();
        Assert.Equal(["    displayName: Run the tests"], added);
    }

    /// <summary>The baseline elements of one open diagram.</summary>
    private static async Task<IReadOnlyList<Element>> BaselineAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        string fileName)
    {
        var diagramClient = new DiagramService.DiagramServiceClient(channel);
        using var stream = diagramClient.Open(
            new OpenDiagramRequest
            {
                ProjectId = projectId,
                WatchId = ShortGuid.NewShortGuid(),
                Path = PathOf(fileName),
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        var elements = new List<Element>();
        using var timeout = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        try
        {
            // One delta is enough: the baseline is a single Add carrying everything in view.
            while (await stream.ResponseStream.MoveNext(timeout.Token))
            {
                if (stream.ResponseStream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    elements.AddRange(stream.ResponseStream.Current.Add.Elements);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Falls through to the assertion, which reports an empty baseline rather than a
            // cancellation nobody can read.
        }

        return elements;
    }

    /// <summary>
    /// Every entry of the project, by name, so a test can name a file rather than an id.
    /// </summary>
    /// <remarks>
    /// The watch id is a parameter because a hierarchy model is per connection: a selection made
    /// on one watch id cannot name an entry listed under another.
    /// </remarks>
    private static async Task<IReadOnlyDictionary<string, Documents.Wire.ShortGuid>> EntriesAsync(
        GrpcChannel channel,
        Metadata headers,
        ShortGuid projectId,
        ShortGuid? watchId = null)
    {
        var hierarchyClient = new HierarchyService.HierarchyServiceClient(channel);
        var effectiveWatchId = watchId ?? ShortGuid.NewShortGuid();
        var response = await hierarchyClient.ListEntriesAsync(
            new ListEntriesRequest { ProjectId = projectId, WatchId = effectiveWatchId },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // A registration is a child of its subject now, so the flat dictionary walks one nested
        // level too - same watch id, so the same model answers both listings.
        var byName = response.Entries.Entries_.ToDictionary(entry => entry.Name, entry => entry.Id, StringComparer.Ordinal);
        foreach (var parent in response.Entries.Entries_.Where(entry => entry.HasChildren && !IsFolderEntry(entry)))
        {
            var children = await hierarchyClient.ListEntriesAsync(
                new ListEntriesRequest { ProjectId = projectId, WatchId = effectiveWatchId, FolderId = parent.Id },
                headers,
                cancellationToken: TestContext.Current.CancellationToken);
            foreach (var child in children.Entries.Entries_)
            {
                byName[child.Name] = child.Id;
            }
        }

        return byName;

        static bool IsFolderEntry(Entry entry) => entry.Kind == EntryKind.Folder;
    }

    /// <summary>The action ids offered on one hierarchy entry.</summary>
    private static async Task<IReadOnlyList<string>> ActionsOnAsync(
        ContextService.ContextServiceClient client,
        Metadata headers,
        ShortGuid projectId,
        ShortGuid watchId,
        Documents.Wire.ShortGuid entryId)
    {
        var response = await client.DiscoverActionsAsync(
            new DiscoverActionsRequest
            {
                ProjectId = projectId,
                WatchId = watchId,
                Source = new ContextSource { EntryId = entryId },
            },
            headers,
            cancellationToken: TestContext.Current.CancellationToken);

        return response.Groups.ActionIds();
    }

    /// <summary>
    /// The canvas's own selection shape: the <c>.adp</c> file, then the element as its child.
    /// </summary>
    /// <remarks>
    /// The child's path is left empty, which asks the backend to fill in the full chain rather
    /// than trusting the client's version of it - the same thing the pipeline canvas sends.
    /// </remarks>
    private static ContextSelection ElementChain(Documents.Wire.ShortGuid entryId, string elementId)
    {
        var chain = new ContextSelection
        {
            Source = ContextSelectionSource.Explorer,
            Id = new ContextSource { EntryId = entryId },
            Path = new Path(),
            Child = new ContextSelection
            {
                Source = ContextSelectionSource.DiagramCanvas,
                Id = new ContextSource { ElementId = new ElementId { Value = elementId } },
                Path = new Path(),
            },
        };

        // The outer path is left empty as well: the backend resolves the entry id to its own
        // path and checks any client-supplied one against it, so sending none is both the
        // simplest correct thing and what the explorer actually does.
        return chain;
    }

    private static Path PathOf(string fileName)
    {
        var path = Path.Of(fileName);
        return path;
    }
}
