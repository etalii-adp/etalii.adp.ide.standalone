using System.Text;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Problems;
using EtAlii.Adp.Projects;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here
using Path = EtAlii.Adp.Documents.Wire.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The designer family's place in the real host, between diagrams and editors
/// (knowledge-designer Requirement 10.2): a designer's registration and its body are that
/// designer's, so neither opens as text and neither is reported as an unknown diagram type,
/// while a file of the same kind that no registration names is still an editor's.
/// </summary>
/// <remarks>
/// A test-double designer type is deployed through the catalog, since the application carries
/// no designer module yet. It has no session: until the designer family has one on this stream,
/// opening its document is refused by name, which is what these tests pin.
/// </remarks>
public class DesignerRoutingFlowTests(DesignerRoutingFlowTests.Host host) : IClassFixture<DesignerRoutingFlowTests.Host>
{
    private static readonly DesignerDefinition Sheet =
        new("fixture/sheet", "Fixture sheet", Formats: [new DesignerFormat("YAML", ".yaml")]);

    private readonly string _projectFolder = Seed(host.NewProjectFolder());

    /// <summary>The shared host, with the test-double designer riding in beside whatever discovery found.</summary>
    public sealed class Host : HostFixture
    {
        protected override void ConfigureServices(IServiceCollection services)
        {
            services.RemoveAll<IDesignerDefinitionCatalog>();
            services.AddSingleton<IDesignerDefinitionCatalog>(provider => new DesignerDefinitionCatalog
            {
                All = [.. provider.GetService<IReadOnlyList<DesignerDefinition>>() ?? [], Sheet],
            });
        }
    }

    private static string Seed(string projectFolder)
    {
        File.WriteAllText(IoPath.Combine(projectFolder, "cities.adp"), "fixture/sheet\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "cities.yaml"), "name: Cities\n");
        File.WriteAllText(IoPath.Combine(projectFolder, "values.yaml"), "replicas: 2\n");
        return projectFolder;
    }

    [Theory]
    [InlineData("cities.yaml")]
    [InlineData("cities.adp")]
    public async Task OpeningADesignersDocument_IsRefusedByName_AndNeverOpensAsText(string file)
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        using var call = client.Open(Request(session.ProjectId, file), session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        var refusal = await Assert.ThrowsAsync<RpcException>(async () =>
        {
            while (await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken))
            {
                // A text editor's stream would deliver the file's content here.
                Assert.Fail($"'{file}' opened as {call.ResponseStream.Current.Add?.Elements.FirstOrDefault()?.Type}.");
            }
        });

        // Assert: refused for good, naming the designer type, rather than shown as raw text.
        Assert.Equal(PermanentRefusal.NotDeployed, refusal.StatusCode);
        Assert.Contains("fixture/sheet", refusal.Status.Detail);
    }

    [Fact]
    public async Task AFileOfTheSameKindThatNoRegistrationNames_StillOpensInItsEditor()
    {
        // Arrange.
        using var session = await FlowSession.OpenAsync(host, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(session.Channel);

        // Act.
        using var call = client.Open(Request(session.ProjectId, "values.yaml"), session.Headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(await call.ResponseStream.MoveNext(TestContext.Current.CancellationToken));

        // Assert: an editor's content, as before the designer family existed.
        var element = Assert.Single(call.ResponseStream.Current.Add.Elements);
        Assert.StartsWith("editor/", element.Type);
        Assert.Equal("replicas: 2\n", Encoding.UTF8.GetString(element.Payload!.Value.Span));
    }

    [Fact]
    public async Task ValidatingTheProject_DoesNotCallADesignersDocumentAnUnknownDiagramType()
    {
        // Arrange: the host's own validator, built by its container - so this also pins that
        // the container hands it the designer router.
        using var _ = host.CreateChannel();
        var validator = host.Services.GetRequiredService<ProjectValidator>();

        // Act.
        var outcome = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);

        // Assert.
        Assert.DoesNotContain(outcome.Problems, problem => problem.RelativePath.StartsWith("cities", StringComparison.Ordinal));
        Assert.Equal(1, outcome.FilesConsidered);
    }

    private static OpenDiagramRequest Request(Documents.Wire.ShortGuid projectId, string file)
    {
        var path = new Path();
        path.Segments.Add(file);
        return new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path };
    }
}
