using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The registered editor session factories, keyed by the editor id they serve. A module registers
/// its factory through its definition's <c>Build</c> delegate; core resolves and never names a module.
/// </summary>
public sealed class EditorSessionFactories(IEnumerable<IEditorSessionFactory> factories)
    : ToolSessionFactories<string, IEditorSessionFactory>(factories, factory => factory.EditorId, StringComparer.Ordinal);
