using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// A selection on the company example, by the contract every element resolver keeps, and a flow,
/// which resolves to its ends.
/// </summary>
public sealed class SankeyContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly SankeyDocumentStore _store = new();

    protected override string Example => SankeyExamples.BrightwaterCoffee;

    protected override IContextSourceResolver CreateResolver() =>
        new SankeyContextSourceResolver(new DiagramFileRouter(new SankeyOnlyCatalog()), _store, new SankeyElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "cafes";

    protected override IReadOnlyList<string> ElementPath => ["Cafés"];

    protected override string ElementType => SankeyElementMapper.NodeType;

    protected override string Gesture => "rel:cafes->revenue";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a Sankey diagram.",
        "That is no longer in this diagram.",
        "New node",
        "New flow");

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
}

internal sealed class SankeyOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
