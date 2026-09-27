namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The step a diagram's time axis is drawn and snapped in, which its document names in a top-level
/// <c>unit:</c> key: a month, a year, a decade or a century.
/// </summary>
/// <param name="Name">What the document writes.</param>
/// <param name="Months">How many months one step spans.</param>
/// <remarks>
/// <b>The dates stay months whatever the unit</b>: a document written in years still says
/// <c>1440-01</c>, so changing the unit changes how the diagram is drawn and never what it states.
/// Every unit is <see cref="GhgScale.UnitsPerStep"/> canvas units wide, which is what lets one
/// module draw a decade of phones and ten thousand years of farming at a readable size.
/// </remarks>
public sealed record GhgTimeUnit(string Name, int Months)
{
    /// <summary>The default, and what a document without a <c>unit:</c> key is drawn in.</summary>
    public static GhgTimeUnit Month { get; } = new("month", 1);

    /// <summary>Twelve months.</summary>
    public static GhgTimeUnit Year { get; } = new("year", 12);

    /// <summary>Ten years.</summary>
    public static GhgTimeUnit Decade { get; } = new("decade", 120);

    /// <summary>A hundred years.</summary>
    public static GhgTimeUnit Century { get; } = new("century", 1200);

    /// <summary>Every unit, from the finest.</summary>
    public static IReadOnlyList<GhgTimeUnit> All { get; } = [Month, Year, Decade, Century];

    /// <summary>The unit a document names, or null when it names none of them.</summary>
    public static GhgTimeUnit? Named(string? name) =>
        All.FirstOrDefault(unit => string.Equals(unit.Name, name?.Trim(), StringComparison.Ordinal));
}
