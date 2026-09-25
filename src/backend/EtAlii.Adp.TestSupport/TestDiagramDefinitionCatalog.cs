using EtAlii.Adp.Documents;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// The diagram definitions a test wants a subject to see, instead of whatever the running
/// process happened to discover. One shared stub: six test classes each carried an identical
/// private copy before the nested types were lifted out, which is six places to change when
/// the catalog interface moves.
/// </summary>
public sealed class TestDiagramDefinitionCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
{
    public TestDiagramDefinitionCatalog(params DiagramDefinition[] definitions)
        : this((IReadOnlyList<DiagramDefinition>)definitions)
    {
    }

    public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
}
