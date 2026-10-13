using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// A selection on the company example: a node, a flow, a drop and a gesture resolve; an unknown id,
/// a wrong client path and a file of another type do not; and a tracked selection follows the
/// document until it is let go.
/// </summary>
public sealed class SankeyContextSourceResolverTests : IDisposable
{
    private readonly SankeyTestFolder _folder = new(SankeyExamples.BrightwaterCoffee, "brightwater-coffee.skv");
    private readonly SankeyDocumentStore _store = new();
    private readonly SankeyContextSourceResolver _resolver;

    public SankeyContextSourceResolverTests()
    {
        _resolver = new SankeyContextSourceResolver(new DiagramFileRouter(new SankeyOnlyCatalog()), _store, new SankeyElementMapper());
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ANode_ResolvesToItsName_WithThePayloadTheCanvasWasSent()
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("cafes")).Level;

        // Assert.
        Assert.Equal(["Cafés"], level.RelativePath);
        Assert.Equal(SankeyElementMapper.NodeType, level.Detail.Element.ElementType);
        Assert.Equal(("cafes", _folder.Body, Diagram.Sankey.Origin), (level.Target.ElementId, level.Target.ResolvedFullPath, level.Target.Origin));
    }

    [Fact]
    public async Task AFlow_ResolvesToItsEnds()
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("revenue->gross-profit")).Level;

        // Assert.
        Assert.Single(level.RelativePath);
        Assert.Contains(" → ", level.RelativePath[0], StringComparison.Ordinal);
        Assert.Equal(SankeyElementMapper.FlowType, level.Detail.Element.ElementType);
    }

    [Theory]
    [InlineData("new:300,700", "New node")]
    [InlineData("rel:cafes->revenue", "New flow")]
    public async Task APlacementAndAGesture_ResolveToWhatTheyPropose(string elementId, string proposed)
    {
        // Act.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(elementId)).Level;

        // Assert.
        Assert.Equal([proposed], level.RelativePath);
        Assert.Equal((elementId, _folder.Body), (level.Target.ElementId, level.Target.ResolvedFullPath));
    }

    [Fact]
    public async Task AnUnknownId_IsRefused()
    {
        // Act.
        var rejected = Assert.IsType<RejectedContextLevel>(await ResolveAsync("nothing-here"));

        // Assert.
        Assert.Equal("That is no longer in this diagram.", rejected.Reason);
    }

    [Fact]
    public async Task AClientPathThatDoesNotMatch_IsRefused()
    {
        // Act.
        var resolution = await ResolveAsync("cafes", ["Packaged coffee"]);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task AnElementWithNoFileAboveIt_IsRefused()
    {
        // Act.
        var resolution = await _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _folder.Folder, Element("cafes"), [], null, TestContext.Current.CancellationToken);

        // Assert.
        Assert.IsType<RejectedContextLevel>(resolution);
    }

    [Fact]
    public async Task ATrackedNode_FollowsARename_AndIsLetGo()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("cafes")).Level;
        var seen = new List<IReadOnlyList<string>?>();
        var tracking = _resolver.Track(level.Target.WatchId, _folder.Folder, level, seen.Add);

        // Act: renamed while tracked, then renamed again after it was let go.
        await RenameCafesAsync("Coffee houses");
        tracking.Dispose();
        await RenameCafesAsync("Coffee bars");

        // Assert.
        Assert.Equal([["Coffee houses"]], seen);
    }

    private async Task RenameCafesAsync(string name)
    {
        var text = await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken);
        var renamed = System.Text.RegularExpressions.Regex.Replace(text, "(  - id: cafes\\r?\\n(?:    .*\\r?\\n)*?    name: )[^\\r\\n]*", "${1}" + name);
        Assert.NotEqual(text, renamed);
        await File.WriteAllTextAsync(_folder.Body, renamed, TestContext.Current.CancellationToken);
        _store.Reload(_folder.Body);
    }

    private Task<ContextLevelResolution> ResolveAsync(string elementId, IReadOnlyList<string>? clientPath = null)
    {
        var file = new ContextResolvedLevel(
            new ContextSource(),
            [System.IO.Path.GetFileName(_folder.Body)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, _folder.Body, IsContainer: false, SourceId: default, _folder.Folder, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

        return _resolver.ResolveAsync(
            ShortGuid.NewShortGuid(), _folder.Folder, Element(elementId), clientPath ?? [], file, TestContext.Current.CancellationToken).AsTask();
    }

    private static ContextSource Element(string elementId) => new() { ElementId = new ElementId { Value = elementId } };
}

internal sealed class SankeyOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
