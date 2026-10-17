namespace EtAlii.Adp.Diagram;

/// <summary>
/// The diagram kind's catalog: the discovered diagram types, which the create and rename commands
/// and the file router look a type up in (code-structure FR-057).
/// </summary>
public interface IDiagramDefinitionCatalog : IToolDefinitionCatalog<DiagramDefinition>;

/// <summary>The production catalog: the diagram definitions the host discovered at startup.</summary>
public sealed class DiagramDefinitionCatalog : ToolDefinitionCatalog<DiagramDefinition>, IDiagramDefinitionCatalog;
