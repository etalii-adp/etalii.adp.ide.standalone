namespace EtAlii.Adp.Designer;

/// <summary>
/// The designer kind's catalog: the discovered designer types, which routing a registration and
/// offering a new document look a designer up in (code-structure FR-057).
/// </summary>
public interface IDesignerDefinitionCatalog : IToolDefinitionCatalog<DesignerDefinition>;

/// <summary>The production catalog: the designer definitions the host discovered at startup.</summary>
public sealed class DesignerDefinitionCatalog : ToolDefinitionCatalog<DesignerDefinition>, IDesignerDefinitionCatalog;
