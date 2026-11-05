using System.Text;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Editor.Wire;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Editor.Json.Tests;

/// <summary>
/// The JSON editor over the real host (json-editor Requirements 2.2, 2.3, 2.5 and 8.2): what the
/// module claims, what it leaves to the diagrams, and its save. The test names no module type: the
/// editor is recognised by the mime its stream carries, as the client recognises it.
/// </summary>
/// <remarks>
/// The Databricks pipeline is the one diagram type deployed with a <c>.json</c> body. It claims
/// <c>.json</c> as a shared extension, so a pipeline settings file becomes its diagram through a
/// registration only - and that registration has to keep opening the diagram now that an editor
/// claims every <c>.json</c> on sight.
/// </remarks>
public class JsonEditorFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string Settings = "{\n  \"name\": \"orders\",\n  \"port\": 8080\n}\n";

    private const string PipelineSettings =
        "{\n  \"name\": \"bronze_to_gold\",\n  \"catalog\": \"lakehouse_dev\",\n  \"schema\": \"gold\",\n" +
        "  \"libraries\": [\n    { \"notebook\": { \"path\": \"transformations/bronze\" } }\n  ]\n}\n";

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "settings.json"), Settings);
        File.WriteAllText(IoPath.Combine(projectFolder, "pipeline.json"), PipelineSettings);
        File.WriteAllText(IoPath.Combine(projectFolder, "pipeline.adp"), "databricks/pipeline\nbody: pipeline.json\n");
        return projectFolder;
    }

    [Fact]
    public async Task AJsonFileNoDiagramClaims_OpensInTheJsonEditor()
    {
        // Arrange (Requirement 2.2: no registration is written - the claim answers).
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);

        // Act.
        var baseline = await DiagramFlow.BaselineAsync(session, "settings.json");

        // Assert: the editor family's one synthetic element, in the JSON editor's mime.
        var element = Assert.Single(baseline);
        Assert.Equal("content", element.Id?.Value);
        Assert.Equal("editor/json", element.Type);
        Assert.Equal(Settings, Encoding.UTF8.GetString(element.Payload!.Value.Span));
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "settings.adp")));
    }

    [Fact]
    public async Task ADatabricksPipelineJson_StillOpensItsDiagram_AndOpenAsTextReachesTheJsonEditor()
    {
        // Arrange (Requirement 2.3).
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act: the registration, as the explorer opens it.
        var diagram = await DiagramFlow.BaselineAsync(session, "pipeline.adp");

        // Assert: the pipeline's own elements, not the editor's synthetic one.
        Assert.Contains(diagram, element => element.Id.Value == "pipeline");
        Assert.DoesNotContain(diagram, element => element.Id.Value == "content");

        // Act: "Open as text" on the body - the forced editor family, resolved by the claim.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = DiagramFlow.PathOf("pipeline.json"), EditorId = "*" },
            session.Headers, cancellationToken: cts.Token);
        var text = await call.ResponseStream.ReadAddAsync(cts.Token);

        // Assert.
        var element = Assert.Single(text);
        Assert.Equal("editor/json", element.Type);
        Assert.Equal(PipelineSettings, Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }

    [Fact]
    public void AfterStartup_TheJsonEditorAloneClaimsJson()
    {
        // Arrange: building the server is what runs discovery.
        using var _ = host.CreateChannel();
        var resolver = host.Services.GetRequiredService<EditorResolver>();

        // Act.
        var claimants = resolver.ClaimantsOf("settings.json");
        var routed = resolver.Resolve("settings.json");

        // Assert (Requirement 2.5): one claimant, so no conflict is reported and the claim routes.
        var claimant = Assert.Single(claimants);
        Assert.Equal("json", claimant.Id);
        Assert.Equal("json", Assert.IsType<EditorRouted>(routed).Definition.Id);
    }

    [Fact]
    public async Task ASave_GoesThroughSaveText_AndIsOneUndoAway()
    {
        // Arrange (Requirement 8.2): the editor open on a watch, as the tab holds it.
        var path = IoPath.Combine(_projectFolder, "undo.json");
        await File.WriteAllTextAsync(path, Settings, TestContext.Current.CancellationToken);
        var before = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var watchId = ShortGuid.NewShortGuid();
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = DiagramFlow.PathOf("undo.json") },
            session.Headers, cancellationToken: cts.Token);
        Assert.Equal("editor/json", Assert.Single(await call.ResponseStream.ReadAddAsync(cts.Token)).Type);

        // Act: a save with a finding in it - a trailing comma - which a finding never blocks.
        var edited = Settings.Replace("8080\n", "8080,\n", StringComparison.Ordinal);
        var saved = await new EditorService.EditorServiceClient(session.Channel).SaveTextAsync(
            new SaveTextRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = DiagramFlow.PathOf("undo.json"), Content = edited },
            session.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: the save landed.
        Assert.Equal("", saved.Error);
        Assert.Equal(edited, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));

        // Act: one undo, on the project's history.
        var undone = await DiagramFlow.ExecuteProjectActionAsync(session, watchId, HistoryContextActionProvider.UndoActionId);

        // Assert: the bytes as they were.
        Assert.True(undone.Accepted, undone.Error);
        Assert.Equal(before, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }
}
