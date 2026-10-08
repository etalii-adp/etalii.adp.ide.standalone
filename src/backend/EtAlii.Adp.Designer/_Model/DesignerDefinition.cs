using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Designer;

/// <summary>
/// What a designer module is: which designer type it serves, how it introduces itself and in
/// which file formats its documents can be created. Each designer project exposes one or more
/// of these through its own static <c>Designer.Definitions</c> array, exactly as a diagram
/// module exposes <c>Diagram.Definitions</c> and an editor module <c>Editor.Definitions</c> -
/// an analogue of both and a shared type with neither: a designer is identified by an origin
/// and a registration file as a diagram is, and stores its document in a format the author
/// chooses when adding it, which neither of the other families does (knowledge-designer
/// Requirement 10.2).
/// </summary>
/// <param name="Origin">
/// The designer type's origin, <c>vendor/type</c>, unique across all tools. It is the first
/// line of a document's <c>.adp</c> registration.
/// </param>
/// <param name="Title">The display name: a name, not a description.</param>
/// <param name="Description">
/// One sentence saying what this designer is for, written from the reader's point of view
/// rather than restating the title.
/// </param>
/// <param name="Icon">
/// The @mdi/font class naming this designer's icon, shown wherever the designer introduces
/// itself. Empty falls back to the generic document mark.
/// </param>
/// <param name="Formats">
/// The file formats a document of this type can be created in, in the order they are offered.
/// Empty for a designer whose documents have one fixed format the module creates itself.
/// </param>
/// <param name="Build">
/// Registers the module's own services into the host, exactly as a diagram or editor
/// definition's <c>Build</c> does. Null for a definition whose module needs nothing registered.
/// </param>
public sealed record DesignerDefinition(
    string Origin,
    string Title,
    string Description = "",
    string Icon = "",
    IReadOnlyList<DesignerFormat>? Formats = null,
    Action<IHostApplicationBuilder>? Build = null)
{
    /// <summary>The formats a document can be created in, in the order they are offered.</summary>
    public IReadOnlyList<DesignerFormat> Formats { get; } = Formats ?? [];
}
