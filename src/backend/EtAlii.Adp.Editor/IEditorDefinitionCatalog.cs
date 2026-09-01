namespace EtAlii.Adp.Editor;

/// <summary>
/// The discovered text editors, as a service rather than a static cache, so code that needs
/// to look an editor up - the resolver, the "Open with…" action - can be handed a
/// test-controlled list. Mirrors <c>IDiagramDefinitionCatalog</c> deliberately: the two
/// families plug in the same way without sharing a type.
/// </summary>
public interface IEditorDefinitionCatalog
{
    IReadOnlyList<EditorDefinition> All { get; }
}

/// <summary>The production catalog: all editor definitions, read at call time because the host fills it after the container is built.</summary>
public sealed class EditorDefinitionCatalog : IEditorDefinitionCatalog
{
    public required IReadOnlyList<EditorDefinition> All { get; init; }
}
