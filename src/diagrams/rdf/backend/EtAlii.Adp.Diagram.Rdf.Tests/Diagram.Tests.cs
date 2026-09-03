using EtAlii.Adp.Backend.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The definition and its routing: bare <c>.ttl</c> and <c>.nt</c> files route to
/// <c>w3c/rdf</c> on sight, through the alternate-extension seam the family carries
/// (rdf-diagram Requirement 2.2).
/// </summary>
public class DiagramTests : IDisposable
{
    private sealed class Catalog : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
    }

    private readonly string _root;
    private readonly DiagramFileRouter _router = new(new Catalog());

    public DiagramTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void TheDefinition_DeclaresBothSerializationsExtensions()
    {
        // Arrange & act & assert.
        Assert.True(Diagram.Rdf.DeclaresExtension(".ttl"));
        Assert.True(Diagram.Rdf.DeclaresExtension(".nt"));
        Assert.False(Diagram.Rdf.DeclaresExtension(".rdf"));
        // Not shared: these extensions belong to this family, so bare bodies route on sight.
        Assert.True(Diagram.Rdf.RoutesBareBody);
    }

    [Theory]
    [InlineData("data.ttl")]
    [InlineData("data.nt")]
    public void ABareFile_OfEitherSerialization_RoutesOnSight(string name)
    {
        // Arrange.
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, "");

        // Act.
        var routing = _router.Route(path);

        // Assert.
        var routed = Assert.IsType<DiagramRouted>(routing);
        Assert.Equal("w3c/rdf", routed.Definition.Origin.Key);
        Assert.True(_router.ClaimsExtensionOf(path));
    }

    [Fact]
    public void AnUnrelatedExtension_IsNotClaimed()
    {
        // Arrange.
        var path = IoPath.Combine(_root, "readme.md");
        File.WriteAllText(path, "");

        // Act & assert.
        Assert.IsType<NotADiagram>(_router.Route(path));
    }
}
