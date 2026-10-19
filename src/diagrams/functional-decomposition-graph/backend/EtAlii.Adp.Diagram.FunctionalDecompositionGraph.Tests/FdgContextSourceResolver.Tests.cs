using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Files;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>A selection on the field service example, by the contract every element resolver keeps.</summary>
public sealed class FdgContextSourceResolverTests : DiagramElementSourceResolverContract
{
    private readonly FdgDocumentStore _store = new(new FileWriter());

    protected override string Example => FieldServiceExample.Path;

    protected override IContextSourceResolver CreateResolver() =>
        new FdgContextSourceResolver(new DiagramFileRouter(new FdgOnlyCatalog()), _store, new FdgElementMapper());

    protected override Task ReloadAsync(string body, CancellationToken cancellationToken) => _store.ReloadAsync(body, cancellationToken);

    protected override string ElementId => "planning";

    protected override IReadOnlyList<string> ElementPath => ["Planning"];

    protected override string ElementType => FdgElementMapper.UiElementType;

    protected override string Gesture => "rel:tick-step->step-list";

    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a functional decomposition graph.",
        "That element is no longer in this graph.",
        "New element",
        "New connection");
}

internal sealed class FdgOnlyCatalog : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All => Diagram.Definitions;
}
