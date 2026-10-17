using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>A selection on the technology trends example, by the contract every element resolver keeps.</summary>
public sealed class GhgContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly GhgDocumentStore _store = new();

    protected override string Example => GhgModuleFiles.Example;

    protected override IContextSourceResolver CreateResolver() =>
        new GhgContextSourceResolver(new DiagramFileRouter(new GhgOnlyCatalog()), _store, new GhgElementMapper());

    protected override void Reload(string body) => _store.Reload(body);

    protected override string ElementId => "steam-engine";

    protected override IReadOnlyList<string> ElementPath => ["Steam engine"];

    protected override string ElementType => GhgElementMapper.TrendType;

    protected override string Gesture => "rel:steam-engine@plateau/bottom/0.3->railways@peak/top/0.1";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a hype cycle graph.",
        "That is no longer in this graph.",
        "New element",
        "New influence");
}

internal sealed class GhgOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
