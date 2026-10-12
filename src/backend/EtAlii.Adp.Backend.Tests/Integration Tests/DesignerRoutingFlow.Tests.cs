using System.Text;
using EtAlii.Adp.Designer;
using EtAlii.Adp.Diagram.Wire;
using EtAlii.Adp.Problems;
using EtAlii.Adp.Projects;
using EtAlii.Adp.TestSupport.Host;
using Grpc.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
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
public class DesignerRoutingFlowTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly DesignerDefinition Sheet =
        new("fixture/sheet", "Fixture sheet", Formats: [new DesignerFormat("YAML", ".yaml")]);

    private readonly string _appDataRoot;
    private readonly string _projectFolder;
    private readonly WebApplicationFactory<Program> _factory;

    public DesignerRoutingFlowTests(WebApplicationFactory<Program> baseFactory)
    {
        _appDataRoot = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.IntegrationTests", Guid.NewGuid().ToString("N"));
        _projectFolder = IoPath.Combine(_appDataRoot, "sample-project");
        Directory.CreateDirectory(_projectFolder);
        File.WriteAllText(IoPath.Combine(_projectFolder, "cities.adp"), "fixture/sheet\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "cities.yaml"), "name: Cities\n");
        File.WriteAllText(IoPath.Combine(_projectFolder, "values.yaml"), "replicas: 2\n");

        _factory = baseFactory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("developer");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProjectStore>();
                services.AddSingleton<IProjectStore>(new FileProjectStore(_appDataRoot));
                services.RemoveAll<IProblemStore>();
                services.AddSingleton<IProblemStore>(provider => new ProblemStore(
                    _appDataRoot,
                    provider.GetRequiredService<Hierarchy.DiagramFileRouter>(),
                    provider.GetRequiredService<Diagram.DiagramValidators>()));

                // The test-double designer rides in beside whatever discovery found.
                services.RemoveAll<IDesignerDefinitionCatalog>();
                services.AddSingleton<IDesignerDefinitionCatalog>(provider => new DesignerDefinitionCatalog
                {
                    All = [.. provider.GetService<IReadOnlyList<DesignerDefinition>>() ?? [], Sheet],
                });
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
        TestFolder.TryDelete(_appDataRoot);
    }

    [Theory]
    [InlineData("cities.yaml")]
    [InlineData("cities.adp")]
    public async Task OpeningADesignersDocument_IsRefusedByName_AndNeverOpensAsText(string file)
    {
        // Arrange.
        using var channel = _factory.CreateGrpcChannel();
        var headers = await channel.SignInAsync(TestContext.Current.CancellationToken);
        var projectId = await channel.AddProjectAsync(headers, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        using var call = client.Open(Request(projectId, file), headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
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
        using var channel = _factory.CreateGrpcChannel();
        var headers = await channel.SignInAsync(TestContext.Current.CancellationToken);
        var projectId = await channel.AddProjectAsync(headers, _projectFolder, TestContext.Current.CancellationToken);
        var client = new DiagramService.DiagramServiceClient(channel);

        // Act.
        using var call = client.Open(Request(projectId, "values.yaml"), headers, deadline: DateTime.UtcNow.AddSeconds(30), cancellationToken: TestContext.Current.CancellationToken);
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
        using var _ = _factory.CreateClient();
        var validator = _factory.Services.GetRequiredService<ProjectValidator>();

        // Act.
        var outcome = await validator.ValidateAsync(new ProjectValidationScope(_projectFolder), TestContext.Current.CancellationToken);

        // Assert.
        Assert.DoesNotContain(outcome.Problems, problem => problem.RelativePath.StartsWith("cities", StringComparison.Ordinal));
        Assert.Equal(1, outcome.FilesConsidered);
    }

    private static OpenDiagramRequest Request(Documents.Wire.ShortGuid projectId, string file)
    {
        var path = Path.Of(file);
        return new OpenDiagramRequest { ProjectId = projectId, WatchId = ShortGuid.NewShortGuid(), Path = path };
    }
}
