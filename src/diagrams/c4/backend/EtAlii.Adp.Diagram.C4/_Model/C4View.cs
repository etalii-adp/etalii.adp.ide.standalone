namespace EtAlii.Adp.Diagram.C4;

/// <summary>One ordered step of a dynamic view, whose numbering carries the order (Requirement 7.5).</summary>
/// <param name="Order">The interaction's number as written, including the nested form (<c>1</c>, <c>1.1</c>).</param>
public sealed record C4Interaction(string SourceId, string DestinationId, string Description, string Order, uint Line);

/// <summary>
/// One view declared in the document's <c>views</c> block: which kind it is, what it is scoped
/// to, and what it shows. Six of the seven C4 diagram types bind one of these
/// (c4-diagrams Requirement 1.1).
/// </summary>
/// <param name="Kind">Which of the six declarable view kinds this is.</param>
/// <param name="Key">The view key, which an <c>.adp</c> file's <c>view:</c> header names.</param>
/// <param name="ScopeId">The element the view is about, or null for a landscape, which has no focus.</param>
/// <param name="Environment">The deployment environment, for a deployment view only.</param>
/// <param name="Title">The title the document declares, or null to fall back to C4's wording.</param>
/// <param name="IncludesEverything">Whether the view declares <c>include *</c>.</param>
/// <param name="Includes">Elements explicitly included.</param>
/// <param name="Excludes">Elements explicitly excluded.</param>
/// <param name="AutoLayout">The autoLayout declaration, or null when the document leaves layout to ADP.</param>
/// <param name="Interactions">The ordered steps, for a dynamic view.</param>
/// <param name="Line">The 1-based line the view declaration begins on.</param>
public sealed record C4View(
    C4ViewKind Kind,
    string Key,
    string? ScopeId,
    string? Environment,
    string? Title,
    bool IncludesEverything,
    IReadOnlyList<string> Includes,
    IReadOnlyList<string> Excludes,
    C4AutoLayout? AutoLayout,
    IReadOnlyList<C4Interaction> Interactions,
    uint Line);

/// <summary>
/// An <c>autoLayout</c> declaration. Where the document carries one it wins over ADP's own
/// arrangement, because the author asked for it explicitly (Requirement 8.2).
/// </summary>
/// <param name="Direction">tb, bt, lr or rl, as written.</param>
public sealed record C4AutoLayout(string Direction, int? RankSeparation, int? NodeSeparation);

/// <summary>
/// A tag-based element style, which is how a model overrides the default palette. C4 is
/// notation independent - the familiar blue and grey is not dictated by it - so ADP's defaults
/// are a theme a document may replace (Requirement 4.8).
/// </summary>
public sealed record C4ElementStyle(
    string Tag,
    string? Background,
    string? Color,
    string? Shape,
    string? Border,
    uint Line);
