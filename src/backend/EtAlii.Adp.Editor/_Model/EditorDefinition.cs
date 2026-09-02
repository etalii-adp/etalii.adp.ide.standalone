using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Editor;

/// <summary>
/// What a text-editor module is: which files it serves and how it introduces itself. Each
/// editor project exposes one or more of these through its own static
/// <c>Editor.Definitions</c> array, exactly as a diagram module exposes
/// <c>Diagram.Definitions</c> - a genuine analogue of <c>DiagramDefinition</c>, deliberately
/// not a shared type with it: an editor claims files by extension or name and has no
/// registration file, so <c>SharedExtension</c>, <c>Subject</c> and a MIME type would be
/// concepts carried onto a type that cannot honour them (modular-text-editors
/// Requirements 2.1-2.5).
/// </summary>
/// <param name="Id">
/// The module's stable identity - <c>"plain"</c>, <c>"markdown"</c> - which the client uses to
/// pick the matching canvas, the way a diagram's MIME type picks its canvas.
/// </param>
/// <param name="Title">The human name shown wherever the editor is offered, e.g. in "Open with…".</param>
/// <param name="Description">
/// One sentence saying what this editor is for, written from the reader's point of view
/// rather than restating the title.
/// </param>
/// <param name="Icon">
/// The @mdi/font class naming this editor's icon, shown wherever the editor introduces
/// itself - "Open with…" among them. Empty falls back to the generic document mark.
/// </param>
/// <param name="Extensions">
/// The extensions this editor claims, dot included; normalised to lower case on construction
/// so lookup never depends on how a module happened to type them. Empty for a name-only or
/// fallback definition.
/// </param>
/// <param name="FileNames">Exact file names this editor claims: <c>"Makefile"</c>, <c>"Dockerfile"</c>.</param>
/// <param name="IsFallback">
/// True for exactly one definition deployment-wide - <c>plain</c>'s - which answers when
/// nothing else claims a file. A fallback claims nothing by construction; it is what remains.
/// </param>
/// <param name="IsDefaultForSharedExtension">
/// The opt-in tie-breaker when two editors legitimately claim one extension: the default opens
/// on activation, the others stay reachable through "Open with…" (Requirement 4.4).
/// </param>
/// <param name="Build">
/// Registers the module's own services into the host, exactly as a diagram definition's
/// <c>Build</c> does. Null for a definition whose module needs nothing registered.
/// </param>
public sealed record EditorDefinition(
    string Id,
    string Title,
    string Description = "",
    string Icon = "",
    IReadOnlyList<string>? Extensions = null,
    IReadOnlyList<string>? FileNames = null,
    bool IsFallback = false,
    bool IsDefaultForSharedExtension = false,
    Action<IHostApplicationBuilder>? Build = null)
{
    /// <summary>The claimed extensions, dot included, lower-cased by construction.</summary>
    public IReadOnlyList<string> Extensions { get; } =
        (Extensions ?? []).Select(extension => extension.ToLowerInvariant()).ToArray();

    /// <summary>The exactly-matched file names. Kept as declared: a Makefile is spelled one way.</summary>
    public IReadOnlyList<string> FileNames { get; } = FileNames ?? [];
}
