using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Mindmap.Tests;

internal sealed class MindmapTestProjectCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
}
