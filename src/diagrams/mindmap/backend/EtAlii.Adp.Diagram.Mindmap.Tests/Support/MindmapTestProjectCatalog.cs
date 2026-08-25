using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Backend.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

internal sealed class MindmapTestProjectCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
{
    public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
}
