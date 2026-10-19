using EtAlii.Adp.Diagram;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using DiagramService = EtAlii.Adp.Diagram.Wire.DiagramService;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;
using Wire = EtAlii.Adp.Diagram.AnsibleStructure.Wire;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The Ansible structure diagram against the real host: a folder registered by one <c>.adp</c>,
/// opened, its baseline read, and the diagram following the folder as it changes.
/// </summary>
/// <remarks>
/// The point of doing this over gRPC rather than against the module's own classes is that this
/// is where the folder-subject novelty actually gets tested: core routes the registration,
/// resolves the session factory by origin, and never learns that the subject is a folder.
/// </remarks>
public class AnsibleStructureFlowTests : IClassFixture<HostFixture>
{
    private readonly HostFixture _host;
    private readonly string _projectFolder;
    private readonly string _infrastructure;

    public AnsibleStructureFlowTests(HostFixture host)
    {
        _host = host;
        _projectFolder = host.NewProjectFolder();
        _infrastructure = IoPath.Combine(_projectFolder, "infrastructure");

        // A real Ansible project inside the project folder, registered the way Add would.
        AnsibleFixture.CopyTo(_infrastructure);
        File.WriteAllText(IoPath.Combine(_infrastructure, "infrastructure.adp"), "ansible/structure\n");
    }

    [Fact]
    public async Task ARegisteredFolder_OpensAndDeliversItsStructure()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        var elements = await BaselineAsync(client, session.Headers, session.ProjectId);

        // Assert.
        // Core routed a registration whose body is itself, resolved this module's session
        // factory by origin, and never learned that the subject is a folder.
        var ids = elements.Select(element => element.Id.Value).ToArray();
        Assert.Contains("playbook:site.yml", ids);
        Assert.Contains("role:nginx", ids);
        Assert.Contains("inventory:inventories/production", ids);
        Assert.Contains(ids, id => id.StartsWith("edge:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ANodesPayload_CarriesWhatTheCanvasNeeds()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        var elements = await BaselineAsync(client, session.Headers, session.ProjectId);
        var nginx = elements.Single(element => element.Id.Value == "role:nginx");
        var payload = Wire.AnsibleElementPayload.Parser.ParseFrom(nginx.Payload.Value.Span);

        // Assert.
        Assert.Equal("nginx", payload.Name);
        // The path the canvas hands to revealPath - project-relative, never absolute.
        Assert.Equal(["roles", "nginx"], payload.ProjectRelativePath);
        Assert.True(payload.Width > 0);
        Assert.Equal(2, payload.Contents.TaskFiles);
    }

    [Fact]
    public async Task AChangeInTheFolder_ReachesAnOpenDiagram()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        var path = Path.Of("infrastructure", "infrastructure.adp");
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            session.Headers, cancellationToken: cts.Token);

        // The baseline first, so the change that follows is the one being observed.
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));

        // Act.
        // A brand-new role appears in the folder. Nothing about the .adp changed.
        var added = IoPath.Combine(_infrastructure, "roles", "redis", "tasks");
        Directory.CreateDirectory(added);
        await File.WriteAllTextAsync(
            IoPath.Combine(added, "main.yml"),
            "---\n- name: Install redis\n  ansible.builtin.package:\n    name: redis\n",
            cts.Token);

        // Assert.
        // Requirement 3.5: the diagram a reader leaves open stays true.
        var arrived = await WaitForElementAsync(call.ResponseStream, "role:redis", cts.Token);
        Assert.True(arrived, "The new role never reached the open diagram.");
    }

    [Fact]
    public async Task OpeningAndReadingADiagram_ChangesNothingInTheFolder()
    {
        // Arrange.
        // The end-to-end form of Requirement 1.1: not just the module's own components, but the
        // whole host serving a real gRPC open.
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);
        var before = Snapshot();

        // Act.
        await BaselineAsync(client, session.Headers, session.ProjectId);

        // Assert.
        Assert.Equal(before.Keys.Order(StringComparer.Ordinal), Snapshot().Keys.Order(StringComparer.Ordinal));
        foreach ((string path, byte[] bytes) in before)
        {
            var bytesToCheck = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
            Assert.True(bytes.SequenceEqual(bytesToCheck), $"{path} changed.");
        }
    }


    [Fact]
    public async Task ARepositionOverTheWire_LandsInTheRegistration_ReopensThere_UndoesBack_AndNeverTouchesAnsiblesFiles()
    {
        // Arrange.
        // The whole cycle helm proved for a chart, for a folder of Ansible: the drag goes over
        // gRPC, the position lands in the .adp, the reopened diagram shows it, undo puts the
        // registration's bytes back, and nothing Ansible owns is written at any point
        // (Requirements 2.2, 2.3, 2.4, 2.5).
        using var session = await FlowSession.OpenAsync(_host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        var registration = IoPath.Combine(_infrastructure, "infrastructure.adp");
        var registrationBefore = await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken);
        var ansibleFilesBefore = Snapshot()
            .Where(entry => !string.Equals(entry.Key, registration, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);

        var path = Path.Of("infrastructure", "infrastructure.adp");
        var watchId = ShortGuid.NewShortGuid();

        // The diagram must be open for the move to reach a session: the unary leg finds it
        // through the viewport registry the stream registered.
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = session.ProjectId, WatchId = watchId, Path = path },
            session.Headers, cancellationToken: cts.Token);
        Assert.True(await call.ResponseStream.MoveNext(cts.Token));

        // Act.
        var moved = await client.MoveElementAsync(
            new MoveElementRequest
            {
                ProjectId = session.ProjectId,
                WatchId = watchId,
                Path = path,
                ElementId = "role:nginx",
                Position = new Point2D { X = 321, Y = 123 },
            },
            session.Headers,
            cancellationToken: cts.Token);

        // Assert 1: the write landed where the layout rule says it must, and nowhere else.
        Assert.Equal(string.Empty, moved.Error);
        Assert.Equal(
            new RegistrationPosition(321, 123),
            RegistrationLayout.Read(registration)["role:nginx"]);

        foreach ((string file, byte[] bytes) in ansibleFilesBefore)
        {
            var now = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
            Assert.True(bytes.SequenceEqual(now), $"The reposition wrote {file}, which Ansible owns.");
        }

        // Assert 2: a diagram opened afresh draws the node where the user put it - the overlay
        // outlives the session, which is what makes the position authored rather than local.
        var reopened = await BaselineAsync(client, session.Headers, session.ProjectId);
        var nginx = reopened.Single(element => element.Id.Value == "role:nginx");
        Assert.Equal(321, nginx.Position.X);
        Assert.Equal(123, nginx.Position.Y);

        // Assert 3: one undo, and the registration is byte-for-byte what it was.
        var history = _host.Services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_projectFolder));
        await history.UndoAsync(TestContext.Current.CancellationToken);
        Assert.Equal(registrationBefore, await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken));
    }

    // ---- helpers -------------------------------------------------------------------------

    private Dictionary<string, byte[]> Snapshot() =>
        Directory.GetFiles(_infrastructure, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private static async Task<bool> WaitForElementAsync(IAsyncStreamReader<Delta> stream, string elementId, CancellationToken cancellationToken)
    {
        try
        {
            while (await stream.MoveNext(cancellationToken))
            {
                if (stream.Current.ActionCase == Delta.ActionOneofCase.Add &&
                    stream.Current.Add.Elements.Any(element => element.Id.Value == elementId))
                {
                    return true;
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // The deadline, which is this helper's way of giving up.
        }

        return false;
    }

    private async Task<IReadOnlyList<Element>> BaselineAsync(
        DiagramService.DiagramServiceClient client, Metadata headers, Documents.Wire.ShortGuid projectId)
    {
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        var path = Path.Of("infrastructure", "infrastructure.adp");
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers, cancellationToken: cts.Token);

        try
        {
            while (await call.ResponseStream.MoveNext(cts.Token))
            {
                if (call.ResponseStream.Current.ActionCase == Delta.ActionOneofCase.Add)
                {
                    return [.. call.ResponseStream.Current.Add.Elements];
                }
            }
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Cancelled)
        {
            // Reading stopped at the deadline, which is how this helper ends normally.
        }

        return [];
    }
}
