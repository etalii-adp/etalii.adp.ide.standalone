using System.Text;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Editor;
using EtAlii.Adp.Editor.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The YAML editor's claims over the real host (yaml-editor Requirement 2.2, 2.3, 2.4 and 2.6).
/// <c>.yml</c> is also the body extension of two diagram types, which claim it only through a
/// registration, so the one extension has three answers: the YAML editor for a bare file, the
/// diagram for a registered one, and the YAML editor again for that diagram's "Open as text".
/// </summary>
public class YamlEditorResolutionTests(HostFixture host) : IClassFixture<HostFixture>
{
    /// <summary>The Azure DevOps pipeline module's own example: a body and the registration beside it.</summary>
    private static string PipelineExample { get; } = RepositoryPaths.Under("src", "diagrams", "azure-devops-pipeline", "examples", "example 1");

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "settings.yaml"), "name: settings\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "compose.yml"), "services: {}\n");

        File.Copy(IoPath.Combine(PipelineExample, "steps-only.yml"), IoPath.Combine(projectFolder, "pipeline.yml"));
        File.Copy(IoPath.Combine(PipelineExample, "steps-only.adp"), IoPath.Combine(projectFolder, "pipeline.adp"));
        return projectFolder;
    }

    [Theory]
    [InlineData("settings.yaml", "name: settings\n")]
    [InlineData("compose.yml", "services: {}\n")]
    public async Task AYamlFileNoDiagramClaims_OpensInTheYamlEditor(string fileName, string content)
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        using var call = Open(diagramClient, session, fileName, editorId: "");
        var baseline = await NextAddAsync(call.ResponseStream, _ => true);

        // Assert: the editor family's one synthetic element, typed by this editor's mime.
        var element = Assert.Single(baseline.Elements);
        Assert.Equal("editor/yaml", element.Type);
        Assert.Equal(content, Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }

    [Fact]
    public async Task ARegisteredPipeline_OpensItsDiagram_AndAsTextInTheYamlEditor_AndASaveReachesItsStore()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var diagramClient = new DiagramService.DiagramServiceClient(session.Channel);

        // Act 1: opened as it is opened from the tree - by its registration - the pipeline is the diagram.
        using var diagramCall = Open(diagramClient, session, "pipeline.adp", editorId: "");
        var diagramBaseline = await NextAddAsync(diagramCall.ResponseStream, add => add.Elements.Count > 0);
        Assert.DoesNotContain(diagramBaseline.Elements, element => element.Id?.Value == "content");

        // Act 2: "Open as text" asks the editor family, which answers by extension.
        using var textCall = Open(diagramClient, session, "pipeline.yml", editorId: "*");
        var textBaseline = await NextAddAsync(textCall.ResponseStream, add => add.Elements.Any(element => element.Id?.Value == "content"));
        var content = Assert.Single(textBaseline.Elements, element => element.Id?.Value == "content");
        Assert.Equal("editor/yaml", content.Type);

        // Act 3: a save in the editor is a change on disk to the diagram, by the family's one path:
        // the module's document store hears it through the shared reload bridge, as it hears any
        // other write (Requirement 2.4 - this editor adds no path of its own).
        var store = host.Services.GetRequiredService<IPipelineDocumentStore>();
        var reloaded = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Changed += OnChanged;
        try
        {
            var original = await File.ReadAllTextAsync(IoPath.Combine(_projectFolder, "pipeline.yml"), TestContext.Current.CancellationToken);
            var edited = original.Replace("displayName: Build", "displayName: Compile", StringComparison.Ordinal);
            var editorClient = new EditorService.EditorServiceClient(session.Channel);
            var saved = await editorClient.SaveTextAsync(
                new SaveTextRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Path = Path.Of("pipeline.yml"), Content = edited },
                session.Headers,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal("", saved.Error);

            // Assert.
            await reloaded.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        }
        finally
        {
            store.Changed -= OnChanged;
        }

        return;

        void OnChanged(object? _, PipelineDocumentChangedEventArgs args)
        {
            if (args.Path.EndsWith("pipeline.yml", StringComparison.Ordinal))
            {
                reloaded.TrySetResult(args.Path);
            }
        }
    }

    [Fact]
    public void TheDeployedEditors_ReportNoClaimConflict_AndRouteBothExtensionsHere()
    {
        // Arrange: the host's own catalog - every editor module the service ships - read by a
        // resolver whose log is captured, since a conflict is reported only in the log (at Error).
        var events = new List<LogEvent>();
        var catalog = host.Services.GetRequiredService<IEditorDefinitionCatalog>();

        // Act.
        var resolver = new EditorResolver(catalog, new LoggerConfiguration().WriteTo.Sink(new ListSink(events)).CreateLogger());

        // Assert.
        Assert.DoesNotContain(events, logEvent => logEvent.Level >= LogEventLevel.Warning);
        Assert.Equal("yaml", Assert.IsType<EditorRouted>(resolver.Resolve("a.yaml")).Definition.Id);
        Assert.Equal("yaml", Assert.IsType<EditorRouted>(resolver.Resolve("b.YML")).Definition.Id);
    }

    private static AsyncServerStreamingCall<Delta> Open(DiagramService.DiagramServiceClient client, FlowSession session, string fileName, string editorId) =>
        client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = session.WatchId, Path = Path.Of(fileName), EditorId = editorId },
            session.Headers,
            deadline: DateTime.UtcNow.AddSeconds(60),
            cancellationToken: TestContext.Current.CancellationToken);

    /// <summary>
    /// Reads the stream until an Add matches; the call's own deadline is the timeout. On giving up
    /// it names what it discarded, so a stream that was silent and one that said the wrong thing
    /// read differently (the lesson of <c>EditorResolutionTests.NextAddAsync</c>).
    /// </summary>
    private static async Task<Add> NextAddAsync(IAsyncStreamReader<Delta> stream, Func<Add, bool> matches)
    {
        var discarded = new List<string>();
        try
        {
            while (await stream.MoveNext(TestContext.Current.CancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add && matches(stream.Current.Add))
                {
                    return stream.Current.Add;
                }

                discarded.Add(stream.Current.ActionCase == Delta.ActionOneofCase.Add
                    ? $"Add({string.Join(",", stream.Current.Add.Elements.Select(element => element.Id?.Value))})"
                    : stream.Current.ActionCase.ToString());
            }
        }
        catch (RpcException exception)
        {
            throw new InvalidOperationException($"The call ended as {exception.StatusCode} having discarded: {string.Join(" | ", discarded)}", exception);
        }

        throw new InvalidOperationException($"The stream ended having discarded: {string.Join(" | ", discarded)}");
    }

    private sealed class ListSink(List<LogEvent> events) : Serilog.Core.ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
