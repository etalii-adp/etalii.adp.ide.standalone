using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>
/// A selection on the automotive example: it resolves, and while it is tracked the sessions are told
/// what this connection selected, which is what they trace from. A drop is never traced.
/// </summary>
public sealed class SupplyChainContextSourceResolverTests : IDisposable
{
    private readonly SupplyChainTestFolder _folder = new(SupplyChainExamples.Automotive, "automotive.supply");
    private readonly SupplyChainDocumentStore _store = new(new FileWriter());
    private readonly SupplyChainSelections _selections = new();
    private readonly ShortGuid _watchId = ShortGuid.NewShortGuid();
    private readonly SupplyChainContextSourceResolver _resolver;

    public SupplyChainContextSourceResolverTests()
    {
        _resolver = new SupplyChainContextSourceResolver(
            new DiagramFileRouter(new SupplyChainOnlyCatalog()), _store, new SupplyChainElementMapper(), _selections);
    }

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task ATrackedNode_IsWhatThisConnectionSelected_UntilItIsLetGo()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("pack-assembly")).Level;

        // Act.
        var tracking = _resolver.Track(_watchId, _folder.Folder, level, _ => { });
        var selected = _selections.Of(_watchId, _folder.Body);
        tracking.Dispose();

        // Assert.
        Assert.Equal("pack-assembly", selected);
        Assert.Null(_selections.Of(_watchId, _folder.Body));
    }

    [Fact]
    public async Task ATrackedDrop_SelectsNothing()
    {
        // Arrange.
        var level = Assert.IsType<ResolvedContextLevel>(await ResolveAsync("new:300,700")).Level;

        // Act.
        using var tracking = _resolver.Track(_watchId, _folder.Folder, level, _ => { });

        // Assert.
        Assert.Equal(["New node"], level.RelativePath);
        Assert.Null(_selections.Of(_watchId, _folder.Body));
    }

    private Task<ContextLevelResolution> ResolveAsync(string elementId)
    {
        var file = new ContextResolvedLevel(
            new ContextSource(),
            [System.IO.Path.GetFileName(_folder.Body)],
            ContextScope.Hierarchy,
            new ContextTarget(ContextScope.Hierarchy, _folder.Body, IsContainer: false, SourceId: default, _folder.Folder, _watchId),
            new ContextLevelDetail(),
            null!);

        return _resolver.ResolveAsync(
            _watchId,
            _folder.Folder,
            new ContextSource { ElementId = new ElementId { Value = elementId } },
            [],
            file,
            TestContext.Current.CancellationToken).AsTask();
    }
}

/// <summary>A selection on the automotive example, by the contract every element resolver keeps.</summary>
public sealed class SupplyChainContextSourceResolverContractTests : DiagramElementSourceResolverContract
{
    private readonly SupplyChainDocumentStore _store = new(new FileWriter());

    protected override string Example => SupplyChainExamples.Automotive;

    protected override IContextSourceResolver CreateResolver() =>
        new SupplyChainContextSourceResolver(
            new DiagramFileRouter(new SupplyChainOnlyCatalog()), _store, new SupplyChainElementMapper(), new SupplyChainSelections());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "pack-assembly";

    protected override IReadOnlyList<string> ElementPath => ["Battery pack assembly"];

    protected override string ElementType => SupplyChainElementMapper.IntegratorType;

    protected override string Gesture => "rel:cell-gigafactory->pack-assembly";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a supply chain diagram.",
        "That is no longer in this diagram.",
        "New node",
        "New flow");
}

internal sealed class SupplyChainOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
