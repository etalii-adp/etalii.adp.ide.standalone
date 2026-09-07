using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// A shared extension is one too common for a type to claim on sight. A bare body carrying one
/// routes nowhere; the same file with an <c>.adp</c> beside it opens. A distinctive extension is
/// unaffected, which is the half of this that could regress silently
/// (azure-pipeline-diagram Requirement 2.2).
/// </summary>
public class DiagramFileRouterSharedExtensionTests : IDisposable
{
    private static readonly DiagramDefinition Pipeline =
        new(new DiagramOrigin("azure-devops", "pipeline"), "Azure DevOps pipeline", Extension: ".yml", SharedExtension: true);

    private static readonly DiagramDefinition Mindmap =
        new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;

    public DiagramFileRouterSharedExtensionTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static DiagramFileRouter Router(params DiagramDefinition[] definitions) => new(new TestDiagramDefinitionCatalog(definitions));

    [Fact]
    public void ABareBody_WithASharedExtension_RoutesNowhere()
    {
        // Arrange: a pipeline file sitting in the repository, which nobody has registered.
        var body = Write("azure-pipelines.yml", "stages:\n  - stage: Build\n");

        // Act.
        var routing = Router(Pipeline, Mindmap).Route(body);

        // Assert.
        Assert.IsType<NotADiagram>(routing);
    }

    [Fact]
    public void ABareBody_ThatIsNotAPipelineAtAll_AlsoRoutesNowhere()
    {
        // Arrange: the case the shared extension exists for - a .yml that is something else.
        var body = Write("docker-compose.yml", "services:\n  web:\n    image: nginx\n");

        // Act.
        var routing = Router(Pipeline).Route(body);

        // Assert: no content was read to decide this; the extension alone is not a claim.
        Assert.IsType<NotADiagram>(routing);
    }

    [Fact]
    public void TheSameBody_WithAnAdpBesideIt_Opens()
    {
        // Arrange: registering the file is what makes it a diagram.
        Write("azure-pipelines.yml", "stages:\n  - stage: Build\n");
        Write("azure-pipelines.adp", "azure-devops/pipeline\n");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(Pipeline).Route(IoPath.Combine(_root, "azure-pipelines.yml")));

        // Assert.
        Assert.Equal(Pipeline.Origin, routed.Definition.Origin);
    }

    [Fact]
    public void ADistinctiveExtension_StillRoutesBare()
    {
        // Arrange: the regression this guard could cause.
        var body = Write("domain.mm", "<map version=\"freeplane 1.11.5\"><node TEXT=\"root\" ID=\"ID_1\"/></map>");

        // Act.
        var routed = Assert.IsType<DiagramRouted>(Router(Pipeline, Mindmap).Route(body));

        // Assert.
        Assert.Equal(Mindmap.Origin, routed.Definition.Origin);
    }

    [Fact]
    public void ADefinition_KnowsWhetherItRoutesABareBody()
    {
        // Arrange & act.
        var shared = Pipeline.RoutesBareBody;
        var distinctive = Mindmap.RoutesBareBody;
        var noSibling = new DiagramDefinition(new DiagramOrigin("uml", "class"), "Class diagram").RoutesBareBody;

        // Assert.
        Assert.False(shared);
        Assert.True(distinctive);
        Assert.False(noSibling);
    }

    [Fact]
    public void AFamilysSharedReadings_NeverWinTheBareBodyFromTheAnchor()
    {
        // Arrange: one vendor's family where the anchor claims the extension and its alternative
        // readings share it - the rdf/owl/skos shape. The catalog is ordered by origin, so a
        // reading whose type sorts before the anchor's would win on order alone; declaring the
        // extension shared is exactly the statement that it must not.
        var anchor = new DiagramDefinition(new DiagramOrigin("w3c", "rdf"), "RDF Graph", Extension: ".ttl", AlternateExtension: ".nt");
        var reading = new DiagramDefinition(
            new DiagramOrigin("w3c", "owl"), "OWL Ontology", Extension: ".ttl", AlternateExtension: ".nt", SharedExtension: true);
        var body = Write("graph.ttl", "@prefix ex: <http://example.org/> .\nex:a ex:knows ex:b .\n");

        // Act: the reading first in catalog order, as the real catalog sorts it.
        var routed = Assert.IsType<DiagramRouted>(Router(reading, anchor).Route(body));

        // Assert: the anchor opens the bare file, whichever order the catalog holds them in.
        Assert.Equal(anchor.Origin, routed.Definition.Origin);
        Assert.Equal(anchor.Origin, Assert.IsType<DiagramRouted>(Router(anchor, reading).Route(body)).Definition.Origin);

        // And the alternate extension follows the same rule.
        var alternate = Write("graph.nt", "<http://example.org/a> <http://example.org/knows> <http://example.org/b> .\n");
        Assert.Equal(anchor.Origin, Assert.IsType<DiagramRouted>(Router(reading, anchor).Route(alternate)).Definition.Origin);
    }
}
