namespace EtAlii.Adp.Designer;

/// <summary>
/// The discovered designer types, as a service rather than a static cache, so code that needs
/// to look a designer up - routing a registration, offering a new document - can be handed a
/// test-controlled list. Mirrors <c>IDiagramDefinitionCatalog</c> and
/// <c>IEditorDefinitionCatalog</c> deliberately: the three families plug in the same way
/// without sharing a type.
/// </summary>
public interface IDesignerDefinitionCatalog
{
    IReadOnlyList<DesignerDefinition> All { get; }
}

/// <summary>The production catalog: the designer definitions the host discovered at startup.</summary>
public sealed class DesignerDefinitionCatalog : IDesignerDefinitionCatalog
{
    public required IReadOnlyList<DesignerDefinition> All { get; init; }
}
