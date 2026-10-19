namespace EtAlii.Adp.Diagram.C4;

/// <summary>What one view draws, derived from the definition of its kind (<see cref="C4Definitions.Drawing"/>).</summary>
/// <param name="Members">The elements the view shows, in model order.</param>
/// <param name="Lines">The lines it draws, in the order of the statements each is drawn for.</param>
/// <param name="Title">The title drawn above it.</param>
/// <param name="Legend">The key drawn below it, one entry per kind among the members.</param>
public sealed record C4Drawing(
    IReadOnlyList<C4Element> Members,
    IReadOnlyList<C4DrawnLine> Lines,
    string Title,
    IReadOnlyList<C4LegendItem> Legend);

/// <summary>One line a view draws: a stored relationship drawn as itself, or one the view derives.</summary>
/// <param name="Id">Its id: a stored relationship's own, else the one the definition computes.</param>
/// <param name="SourceId">The element it starts at, as its statement writes it where the line is that statement's own.</param>
/// <param name="DestinationId">The element it ends at, likewise.</param>
/// <param name="Description">What it says.</param>
/// <param name="Technology">How, where it says.</param>
/// <param name="Order">A dynamic view's interaction number; empty everywhere else.</param>
public sealed record C4DrawnLine(string Id, string SourceId, string DestinationId, string Description, string Technology, string Order);

/// <summary>One entry of a view's key: its text, and the first member it stands for, which its swatch is painted from.</summary>
public sealed record C4LegendItem(string Label, C4Element First);
