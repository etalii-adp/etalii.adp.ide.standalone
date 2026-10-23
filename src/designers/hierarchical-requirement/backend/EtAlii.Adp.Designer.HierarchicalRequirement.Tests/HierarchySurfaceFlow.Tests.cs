using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DesignerService = EtAlii.Adp.Designer.Wire.DesignerService;
using IoPath = System.IO.Path;
using Path = EtAlii.Adp.Documents.Wire.Path;
using Proto = EtAlii.Adp.Designer.Wire;
using WorkspaceService = EtAlii.Adp.Diagram.Wire.WorkspaceService;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The Hierarchical Requirement Designer through the host (spec 029 US1 scenarios 2 to 4): a Markdown file with the
/// envelope opens in the designer, an ordinary one in the Markdown editor and the Agent Behavior Modelling document in its
/// diagram; an existing enveloped file added as a designer document is taken as it is; the page shows every entry and
/// opening it writes nothing.
/// </summary>
/// <remarks>
/// Nothing here is a test double. The host is the one <c>Program.cs</c> builds, the module is the one discovery found,
/// and the files are copies of the examples that are shipped.
/// </remarks>
public sealed class HierarchySurfaceFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder("requirements");

    /// <summary>The designer's example, an ordinary Markdown file and the Agent Behavior Modelling document, side by side.</summary>
    private void Seed()
    {
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"), IoPath.Combine(_projectFolder, "web-shop.md"));
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.adp"), IoPath.Combine(_projectFolder, "web-shop.adp"));
        File.WriteAllText(IoPath.Combine(_projectFolder, "notes.md"), "# Notes\r\n\r\n- A bullet that is no requirement.\r\n");
        var behaviour = RepositoryPaths.Under("src", "examples", "diagrams", "agent-behavior-modelling", "bug-fixer");
        File.Copy(IoPath.Combine(behaviour, "bug-fixer.md"), IoPath.Combine(_projectFolder, "bug-fixer.md"));
        File.Copy(IoPath.Combine(behaviour, "bug-fixer.adp"), IoPath.Combine(_projectFolder, "bug-fixer.adp"));
    }

    [Fact]
    public void EachMarkdownFile_OpensInTheToolItIsFor()
    {
        // Arrange.
        Seed();
        var router = host.Services.GetRequiredService<ToolRouter>();

        // Act.
        var requirements = router.Route(IoPath.Combine(_projectFolder, "web-shop.md"), _projectFolder);
        var notes = router.Route(IoPath.Combine(_projectFolder, "notes.md"), _projectFolder);
        var behaviour = router.Route(IoPath.Combine(_projectFolder, "bug-fixer.md"), _projectFolder);
        var registration = router.Route(IoPath.Combine(_projectFolder, "bug-fixer.adp"), _projectFolder);
        File.Copy(IoPath.Combine(_projectFolder, "web-shop.md"), IoPath.Combine(_projectFolder, "written-by-an-agent.md"));
        var unregistered = router.Route(IoPath.Combine(_projectFolder, "written-by-an-agent.md"), _projectFolder);

        // Assert: the envelope decides the designer, and nothing else is taken from the editor or the diagram.
        Assert.Equal(Designer.Origin, Assert.IsType<DesignerRouted>(requirements).Definition.Origin);
        Assert.Equal("markdown", Assert.IsType<EditorRouted>(notes).Definition.Id);
        Assert.Equal(Designer.Origin, Assert.IsType<DesignerRouted>(unregistered).Definition.Origin);
        Assert.IsNotType<DesignerRouted>(behaviour);
        Assert.Equal("etalii/agent-behavior-modelling", Assert.IsType<DiagramRouted>(registration).Definition.Origin.ToString());
    }

    [Fact]
    public void AnExistingEnvelopedFile_AddedAsADesignerDocument_IsTakenAsItIs()
    {
        // Arrange.
        Seed();
        var definition = host.Services.GetRequiredService<IDesignerDefinitionCatalog>().All.Single(candidate => candidate.Origin == Designer.Origin);
        var format = definition.Formats.Single(candidate => candidate.Extension == ".md");
        var options = host.Services.GetRequiredService<DesignerAddOptions>();

        // Act.
        var enveloped = options.Plan(definition, format, _projectFolder, "web-shop");
        var ordinary = options.Plan(definition, format, _projectFolder, "notes");

        // Assert: the enveloped file is the document already, and nothing is to be written; the ordinary one is refused with the line it needs.
        Assert.Equal("", enveloped.Error);
        Assert.True(enveloped.OpensExisting);
        Assert.Equal(IoPath.Combine(_projectFolder, "web-shop.md"), enveloped.Destination);
        Assert.Contains("<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->", ordinary.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDocument_IsShownWhole_WritingNothing_AndAFoldIsNoUndoStep()
    {
        // Arrange.
        Seed();
        var body = IoPath.Combine(_projectFolder, "web-shop.md");
        var registration = IoPath.Combine(_projectFolder, "web-shop.adp");
        var bodyBefore = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var registrationBefore = await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken);
        using var flow = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var workspace = new WorkspaceService.WorkspaceServiceClient(flow.Channel);
        var designer = new DesignerService.DesignerServiceClient(flow.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watch = workspace.Watch(new WatchWorkspaceRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId }, flow.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The workspace stream ended.");

        // Act: the page opened by its body.
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();
        await designer.OpenHierarchySurfaceAsync(
            new Proto.OpenHierarchySurfaceRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId, StreamId = streamId, Path = Path.Of("web-shop.md") },
            flow.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var opened = await ReadSurfaceAsync(watch.ResponseStream, streamId, message => message.Model is not null, cts.Token);

        // Assert: every item and requirement of the example, the placeholder by its line, with the folds its registration keeps, editable.
        var model = opened.Model;
        Assert.Equal("Web shop", model.Title);
        Assert.Equal("", model.ReadOnlyReason);
        Assert.Equal("open", model.Cause);
        Assert.DoesNotContain(model.Findings, finding => finding.Severity == "error");
        Assert.Equal(6, model.Nodes.Count(node => node.Role == "level"));
        Assert.Equal(11, model.Nodes.Count(node => node.Role == "entry"));
        Assert.Equal(["REQ-NEW"], model.Nodes.Where(node => node.Id != node.Number).Select(node => node.Number));
        Assert.Equal(["ITEM-0003", "REQ-0002"], model.Nodes.Where(node => node.Collapsed).Select(node => node.Id).Order(StringComparer.Ordinal));
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        Assert.Equal(registrationBefore, await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken));

        // Act: a fold.
        Documents.Wire.ShortGuid editId = ShortGuid.NewShortGuid();
        var accepted = await designer.HierarchySurfaceGestureAsync(
            new Proto.HierarchySurfaceGestureRequest { WatchId = flow.WatchId, StreamId = streamId, EditId = editId, Operation = "toggleCollapsed", TargetId = "REQ-0001" },
            flow.Headers,
            cancellationToken: TestContext.Current.CancellationToken);

        // Assert: written to the registration alone, sent as a new model, and no step of the project's history.
        Assert.Equal("", accepted.Error);
        var outcome = await ReadSurfaceAsync(watch.ResponseStream, streamId, message => message.Outcome is { } settled && settled.EditId.Equals(editId), cts.Token);
        Assert.True(outcome.Outcome.Written, outcome.Outcome.Error);
        await ReadSurfaceAsync(watch.ResponseStream, streamId, message => message.Model?.Nodes.Single(node => node.Id == "REQ-0001").Collapsed == true, cts.Token);
        Assert.Contains("REQ-0001: yes", await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(bodyBefore, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
        var history = host.Services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_projectFolder));
        Assert.False((await history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Contains("REQ-0001: yes", await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act and assert: closed, and the connection stays.
        await designer.CloseHierarchySurfaceAsync(new Proto.CloseHierarchySurfaceRequest { WatchId = flow.WatchId, StreamId = streamId }, flow.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.NotNull(host.Services.GetRequiredService<Workspace.WorkspaceConnections>().Find(flow.WatchId, flow.UserId));
    }

    [Fact]
    public async Task ADocumentWithoutARegistration_OpensByItsEnvelope()
    {
        // Arrange: the body alone, as an agent writes it (found in the browser check of pull request 5).
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"), IoPath.Combine(_projectFolder, "agent.md"));
        using var flow = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var workspace = new WorkspaceService.WorkspaceServiceClient(flow.Channel);
        var designer = new DesignerService.DesignerServiceClient(flow.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watch = workspace.Watch(new WatchWorkspaceRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId }, flow.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The workspace stream ended.");

        // Act.
        Documents.Wire.ShortGuid streamId = ShortGuid.NewShortGuid();
        await designer.OpenHierarchySurfaceAsync(
            new Proto.OpenHierarchySurfaceRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId, StreamId = streamId, Path = Path.Of("agent.md") },
            flow.Headers,
            cancellationToken: TestContext.Current.CancellationToken);
        var opened = await ReadSurfaceAsync(watch.ResponseStream, streamId, message => message.Model is not null, cts.Token);

        // Assert: the page, and still no registration beside it.
        Assert.Equal("Web shop", opened.Model.Title);
        Assert.NotEmpty(opened.Model.Nodes);
        Assert.False(File.Exists(IoPath.Combine(_projectFolder, "agent.adp")));
    }

    [Fact]
    public async Task AnOrdinaryMarkdownFile_IsRefusedAsAPage()
    {
        // Arrange.
        Seed();
        using var flow = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var designer = new DesignerService.DesignerServiceClient(flow.Channel);
        var workspace = new WorkspaceService.WorkspaceServiceClient(flow.Channel);
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var watch = workspace.Watch(new WatchWorkspaceRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId }, flow.Headers, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await watch.ResponseStream.MoveNext(cts.Token), "The workspace stream ended.");

        // Act.
        var refused = await Assert.ThrowsAsync<RpcException>(async () => await designer.OpenHierarchySurfaceAsync(
            new Proto.OpenHierarchySurfaceRequest { ProjectId = flow.ProjectId, WatchId = flow.WatchId, StreamId = (Documents.Wire.ShortGuid)ShortGuid.NewShortGuid(), Path = Path.Of("notes.md") },
            flow.Headers,
            cancellationToken: TestContext.Current.CancellationToken));

        // Assert: a permanent answer, so the tab says it rather than retrying.
        Assert.NotEqual(StatusCode.Unavailable, refused.StatusCode);
    }

    /// <summary>The first message of one page's stream that is what is waited for; whatever arrives before it is passed over.</summary>
    private static async Task<Proto.HierarchySurfaceStreamMessage> ReadSurfaceAsync(IAsyncStreamReader<WorkspaceMessage> stream, Documents.Wire.ShortGuid streamId, Func<Proto.HierarchySurfaceStreamMessage, bool> wanted, CancellationToken cancellationToken) =>
        (await stream.FirstAsync(message => message.MessageCase == WorkspaceMessage.MessageOneofCase.HierarchySurface && message.HierarchySurface.StreamId.Equals(streamId) && wanted(message.HierarchySurface), cancellationToken)).HierarchySurface;
}
