using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>One connection entry, and the lines that declare it.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Type">The relation, verbatim. An unknown value survives and is reported.</param>
/// <param name="From">The source element's id. Naming no element is a dangling reference.</param>
/// <param name="To">The target element's id.</param>
/// <param name="Name">Its label.</param>
/// <param name="Description">Prose about the connection. <b>Never sent to the client</b>.</param>
/// <param name="Range">The lines this entry occupies.</param>
public sealed record FdgConnection(
    string Id,
    string Type,
    string From,
    string To,
    string Name,
    string Description,
    LineRange Range);

/// <summary>
/// The five relations, stated once. <b>The type names the relation</b>, because the rules are per
/// relation rather than per pair of element types, and these are the client's relation ids.
/// </summary>
/// <remarks>
/// Strings for the same reason as <see cref="FdgElementTypes"/>: an unknown relation has to
/// survive a round trip so the author gets their line back, and be reported rather than dropped.
/// </remarks>
public static class FdgConnectionTypes
{
    public const string UiChild = "ui-child";
    public const string OwnsAction = "owns-action";
    public const string OwnsData = "owns-data";
    public const string OwnsFunction = "owns-function";
    public const string Shows = "shows";

    /// <summary>The five, in the order the design lists them.</summary>
    public static readonly IReadOnlyList<string> All = [UiChild, OwnsAction, OwnsData, OwnsFunction, Shows];

    /// <summary>Whether the text names one of the five.</summary>
    public static bool IsKnown(string type) => All.Contains(type, StringComparer.Ordinal);
}
