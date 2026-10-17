using System.Text.RegularExpressions;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>A selection on the simple graph, by the contract every element resolver keeps, and what is its own.</summary>
public sealed class DependencyGraphContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly DependencyGraphDocumentStore _store = new();

    protected override string Example => DependencyGraphDocumentStoreTests.Simple;

    protected override IContextSourceResolver CreateResolver() =>
        new DependencyGraphContextSourceResolver(new DiagramFileRouter(new DependencyGraphOnlyCatalog()), _store, new DependencyGraphElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "k7Qv2mXa";

    protected override IReadOnlyList<string> ElementPath => ["API gateway"];

    protected override string ElementType => DependencyGraphElementMapper.NodeType;

    protected override string Gesture => "rel:k7Qv2mXa->b3Rt9wYz";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a dependency graph.",
        "That element is no longer in this graph.",
        "New node",
        "New node");

    /// <summary>A graph names a node by its <c>label:</c>.</summary>
    protected override async Task<IReadOnlyList<string>> RenameAsync(string body, string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(body, cancellationToken);
        var renamed = Regex.Replace(text, "(?<=label: )(API gateway|Renamed once)", name, RegexOptions.None, TimeSpan.FromSeconds(1));
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(body, renamed, cancellationToken);
        return [name];
    }

    [Fact]
    public async Task ADependency_ResolvesToo()
    {
        // Act: both live on the same stream and are selected through the same channel.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("c1Ld4pQn")).Level;

        // Assert.
        Assert.Equal(["verifies tokens with"], level.RelativePath);
        Assert.Equal(DependencyGraphElementMapper.RelationType, level.Detail.Element.ElementType);
    }

    [Fact]
    public async Task AnUnlabelledDependency_IsNamedByItsDirection()
    {
        // Arrange.
        using var folder = TestFolder.Create("dgr-unlabelled");
        var path = folder.Combine("bare" + Diagram.DocumentExtension);
        await File.WriteAllTextAsync(path, "dependencies: 1\nelements:\n  - id: aaa\n    x: 0\n  - id: bbb\n    x: 100\nrelations:\n  - id: ccc\n    from: aaa\n    to: bbb\n", TestContext.Current.CancellationToken);

        // Act.
        var resolution = await ResolveAsync("ccc", file: path);

        // Assert: the arrow in the fallback name is the direction, not decoration: a selection
        // that read "bbb → aaa" would describe the opposite dependency.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(["aaa → bbb"], level.RelativePath);
    }

    [Fact]
    public async Task ARowPlacement_ResolvesToTheNodeAboutToExist()
    {
        // Arrange: a graph's drop names a position across and a whole row.
        var placement = GestureIds.RowPlacement(-180.5, 4);

        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(placement)).Level;

        // Assert.
        Assert.Equal(["New node"], level.RelativePath);
        Assert.Equal(placement, level.Target.ElementId);
    }
}

/// <summary>
/// A catalog carrying only this module's definition, so the router routes the fixture files
/// without the process-wide discovery cache being filled by a test.
/// </summary>
internal sealed class DependencyGraphOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
