using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Hierarchy;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 13's half of the resolver: a drop's placement and a finished connect gesture resolve, so the
/// action they carry can be executed against them. Without it, every palette drop and every drawn
/// connection is refused before any provider is asked.
/// </summary>
public sealed class GhgContextSourceResolverTests : IDisposable
{
    private readonly string _folder = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.GhgResolverTests", Guid.NewGuid().ToString("N"));
    private readonly GhgDocumentStore _store = new();

    public GhgContextSourceResolverTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(GhgModuleFiles.Example, Body);
    }

    private string Body => IoPath.Combine(_folder, "technology-trends.ghg");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    private async Task<ContextLevelResolution> ResolveAsync(string elementId)
    {
        var resolver = new GhgContextSourceResolver(new DiagramFileRouter(new GhgOnlyCatalog()), _store, new GhgElementMapper());
        var file = new ContextResolvedLevel(
            new ContextSource(),
            [IoPath.GetFileName(Body)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, Body, IsContainer: false, SourceId: default, _folder, ShortGuid.NewShortGuid()),
            new ContextLevelDetail(),
            null!);

        return await resolver.ResolveAsync(
            ShortGuid.NewShortGuid(),
            _folder,
            ContextSelectionSource.DiagramCanvas,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            [],
            file,
            TestContext.Current.CancellationToken);
    }

    [Theory]
    [InlineData("new:300,700")]
    [InlineData("rel:steam-engine@plateau/bottom/0.3->railways@peak/top/0.1")]
    public async Task APlacementAndAGesture_Resolve_ToTheirOwnId_OnTheBody(string elementId)
    {
        // Act.
        var resolution = await ResolveAsync(elementId);

        // Assert.
        var level = Assert.IsType<ResolvedContextLevel>(resolution).Level;
        Assert.Equal(elementId, level.Target.ElementId);
        Assert.Equal(Body, level.Target.ResolvedFullPath);
    }

    [Fact]
    public async Task AnElement_StillResolves_AndAnUnknownIdStillDoesNot()
    {
        // Act.
        var element = await ResolveAsync("steam-engine");
        var unknown = await ResolveAsync("nothing-here");

        // Assert.
        Assert.Equal(["Steam engine"], Assert.IsType<ResolvedContextLevel>(element).Level.RelativePath);
        Assert.IsType<RejectedContextLevel>(unknown);
    }
}

internal sealed class GhgOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
