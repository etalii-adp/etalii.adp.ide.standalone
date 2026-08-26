namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// A positioned element of the map: a `component`, an `anchor` or a `submap` (Requirement 5.1).
/// </summary>
/// <param name="Name">
/// The name the document gives it, which is also the handle every other statement refers to it
/// by - links, `evolve`, pipeline membership. Not an identity: renaming keeps the identity and
/// rewrites every reference (Requirement 4.4).
/// </param>
/// <param name="Kind">Which of the three statement kinds declared it.</param>
/// <param name="Position">Where the author put it, in the document's own axis order.</param>
/// <param name="Line">The 1-based line that declares it, for splicing and for problem locations.</param>
/// <param name="Decorators">The decorators it carries, in written order. Empty is the common case.</param>
/// <param name="Inertia">Whether it resists movement. A boolean, not a decorator (Requirement 6.2).</param>
/// <param name="LabelOffset">
/// The `label [dx, dy]` offset in <b>pixels</b> - a property of the format, which ADP reproduces
/// rather than corrects (Requirement 5.5). Null when the document gives none.
/// </param>
/// <param name="Url">The `url(...)` reference this element carries, or empty.</param>
public sealed record WardleyComponent(
    string Name,
    WardleyElementKind Kind,
    WardleyCoordinate Position,
    uint Line,
    IReadOnlyList<WardleyDecorator> Decorators,
    bool Inertia = false,
    WardleyLabelOffset? LabelOffset = null,
    string Url = "");
