namespace EtAlii.Adp;

/// <summary>
/// One designer type a designer module declares: the placeholder for the designer family, the
/// form-based kind of tool beside diagrams and editors (docs/creating-a-designer-module.md).
/// No designer exists yet, so this carries only what every tool type has - an id and the
/// display name - and grows with the first designer module.
/// </summary>
/// <param name="Id">The designer type's origin, <c>vendor/type</c>, unique across all tools.</param>
/// <param name="Title">The display name: a name, not a description.</param>
public sealed record DesignerDefinition(string Id, string Title);
