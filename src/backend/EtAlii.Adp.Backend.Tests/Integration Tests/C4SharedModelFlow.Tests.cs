using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// C4's central premise, over the real host: two <c>.adp</c> files naming one <c>.dsl</c> are
/// two views of one model, not two drawings (c4-diagrams Requirements 1.2, 2.4, 2.6).
/// </summary>
public class C4SharedModelFlowTests(HostFixture host) : IClassFixture<HostFixture>
{
    private const string Model = """
        workspace "Bank" {
            model {
                u = person "Customer" "A customer."
                s = softwareSystem "Banking" "Does banking." {
                    web = container "Web" "Serves pages." "React"
                }
                u -> web "Uses" "HTTPS"
            }
            views {
                systemContext s "context" {
                    include *
                }
                container s "containers" {
                    include *
                }
            }
        }
        """;

    private readonly string _projectFolder = Seed(host.NewProjectFolder("c4-project"));

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "banking.dsl"), Model);
        // Two registrations, one body: the shape Requirement 2.4 exists for.
        File.WriteAllText(IoPath.Combine(projectFolder, "context.adp"), "c4/context\nbody: banking.dsl\nview: context\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "containers.adp"), "c4/container\nbody: banking.dsl\nview: containers\n");
        return projectFolder;
    }

    [Fact]
    public async Task TwoRegistrationsOverOneDocument_EachOpenTheirOwnView()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Arrange, continued.
        var context = await ElementIdsAsync(client, session.Headers, session.ProjectId, "context.adp");
        var containers = await ElementIdsAsync(client, session.Headers, session.ProjectId, "containers.adp");

        // Act.
        // A context view shows people and software systems; a container view shows the
        // containers inside its system. One document, two different answers.
        Assert.Contains("u", context);
        Assert.Contains("s", context);
        Assert.DoesNotContain("web", context);

        // Assert.
        Assert.Contains("web", containers);
        // The system in scope is the boundary on a container view, not a box.
        Assert.DoesNotContain("s", containers);
    }

    [Fact]
    public async Task ABareDslWithNoRegistration_OpensItsFirstDeclaredView()
    {
        // Arrange.
        // Requirement 2.6: a document dropped into the project on its own is still openable,
        // and shows the first view it declares.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        var ids = await ElementIdsAsync(client, session.Headers, session.ProjectId, "banking.dsl");

        // Assert.
        Assert.Contains("s", ids);
        Assert.DoesNotContain("web", ids);
    }

    [Fact]
    public async Task EveryViewCarriesItsOwnTitle_SoTwoTabsOfOneModelAreTellableApart()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        var context = await TitleOfAsync(client, session.Headers, session.ProjectId, "context.adp");
        var containers = await TitleOfAsync(client, session.Headers, session.ProjectId, "containers.adp");

        // Assert.
        Assert.Equal("System Context diagram for Banking", context);
        Assert.Equal("Container diagram for Banking", containers);
    }

    // Deleting one view while leaving the shared model alone is asserted in
    // DiagramFilePairTests.Delete_ARegistrationNamingASharedBody_LeavesTheBodyAlone, where the
    // ownership rule that guarantees it lives. Deletion reaches the backend through a context
    // action rather than a hierarchy RPC, so reproducing it here would exercise the action
    // plumbing rather than the property itself.

    // ---- helpers ----------------------------------------------------------------------

    /// <summary>The elements a diagram's baseline delivers, by id.</summary>
    private static async Task<IReadOnlyList<string>> ElementIdsAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Documents.Wire.ShortGuid projectId,
        string fileName)
    {
        var elements = await BaselineAsync(client, headers, projectId, fileName);
        return elements.Select(element => element.Id.Value).ToArray();
    }

    /// <summary>The title the view's own element carries - what C4 requires every diagram to show.</summary>
    private static async Task<string> TitleOfAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Documents.Wire.ShortGuid projectId,
        string fileName)
    {
        var elements = await BaselineAsync(client, headers, projectId, fileName);
        var view = elements.Single(element => element.Type == "c4/model+view");
        return Diagram.C4.C4ViewPayload.Parser.ParseFrom(view.Payload.Value.Span).Title;
    }

    /// <summary>Opens a diagram and reads the one add that carries its baseline.</summary>
    private static async Task<IReadOnlyList<Element>> BaselineAsync(
        DiagramService.DiagramServiceClient client,
        Metadata headers,
        Documents.Wire.ShortGuid projectId,
        string fileName)
    {
        using var cts = FlowSession.CreateMessageTimeout(TestContext.Current.CancellationToken);

        var path = Path.Of(fileName);
        using var call = client.Open(
            new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path },
            headers, cancellationToken: cts.Token);

        try
        {
            // The stream stays open after the baseline; one message is all this needs.
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
