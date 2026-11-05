namespace EtAlii.Adp.Designer;

/// <summary>A level or an entry of a hierarchy surface.</summary>
/// <param name="Id">
/// What a gesture names it by: its number when that is usable, otherwise <c>line/&lt;n&gt;</c>, the line of its bullet,
/// valid for this model only.
/// </param>
/// <param name="Number">Its number as written, a placeholder included.</param>
/// <param name="Role">One of <see cref="HierarchySurfaceRoles"/>.</param>
/// <param name="Type">The specification's type.</param>
/// <param name="ParentId">The id of the level it stands in, or empty at the top.</param>
/// <param name="Description">Its lines joined by LF, the indentation taken off.</param>
/// <param name="Labels">The values of each label it carries.</param>
/// <param name="References">The numbers its description names.</param>
/// <param name="Collapsed">Whether the registration keeps it collapsed.</param>
public sealed record HierarchySurfaceNode(
    string Id,
    string Number,
    string Role,
    string Type,
    string ParentId,
    string Title,
    string Description,
    IReadOnlyList<HierarchySurfaceLabelValues> Labels,
    IReadOnlyList<HierarchySurfaceReference> References,
    bool Collapsed);
