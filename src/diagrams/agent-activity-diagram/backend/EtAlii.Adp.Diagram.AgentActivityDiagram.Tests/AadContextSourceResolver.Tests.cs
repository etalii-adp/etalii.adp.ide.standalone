using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>A selection on the one-day example, by the contract every element resolver keeps, and what is its own.</summary>
public sealed class AadContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly AadDocumentStore _store = new();

    protected override string Example => AadDocumentStoreTests.OneDay;

    protected override IContextSourceResolver CreateResolver() =>
        new AadContextSourceResolver(new DiagramFileRouter(new AadOnlyCatalog()), _store, new AadElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "s-knowledge";

    protected override IReadOnlyList<string> ElementPath => ["Knowledge designer"];

    protected override string ElementType => AadElementMapper.SpecificationType;

    protected override string Gesture => "rel:a-dev2->s-knowledge";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not an agent activity diagram.",
        "That is no longer in this diagram.",
        "New element",
        "New relation");

    [Fact]
    public async Task TheDiagramsSwitch_AndAGroupOfAnElement_ResolveToWhatTheyAre()
    {
        // Act.
        var view = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(AadElementMapper.ViewId)).Level;
        var group = Assert.IsType<ResolvedContextLevel>(await ResolveAsync(AadGroupTarget.Of("s-knowledge", "finished"))).Level;

        // Assert.
        Assert.Equal(["Diagram"], view.RelativePath);
        Assert.Equal(["Group"], group.RelativePath);
        Assert.Equal(AadGroupTarget.Of("s-knowledge", "finished"), group.Target.ElementId);
    }

    [Fact]
    public async Task AGroupOfAnElementThatIsGone_IsRefused()
    {
        // Act.
        var resolution = await ResolveAsync(AadGroupTarget.Of("s-gone", "finished"));

        // Assert.
        Assert.Equal(Texts.Gone, Assert.IsType<RejectedContextLevel>(resolution).Reason);
    }
}

internal sealed class AadOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
