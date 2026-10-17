namespace EtAlii.Adp.Editor;

/// <summary>
/// The editor kind's catalog: the discovered text editors, which the resolver and the "Open with…"
/// action look an editor up in (code-structure FR-057).
/// </summary>
public interface IEditorDefinitionCatalog : IToolDefinitionCatalog<EditorDefinition>;

/// <summary>The production catalog: the editor definitions the host discovered at startup.</summary>
public sealed class EditorDefinitionCatalog : ToolDefinitionCatalog<EditorDefinition>, IEditorDefinitionCatalog;
