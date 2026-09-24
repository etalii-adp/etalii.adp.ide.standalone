using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>One element entry, and the lines that declare it.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Type">Its declared type, verbatim. Unknown values survive and are reported.</param>
/// <param name="Name">Its name. A Comment has none; a <c>name</c> on one survives and is reported.</param>
/// <param name="Description">Prose about the element. <b>Never sent to the client</b> (Requirement 7.1).</param>
/// <param name="Text">A Comment's body, from a block scalar, with its newlines.</param>
/// <param name="X">The LEFT edge in canvas units, not the centre.</param>
/// <param name="Y">The TOP edge in canvas units, not the centre.</param>
/// <param name="Width">Its width. <see cref="FdgGeometry.MinimumWidth"/> is the floor the rules check.</param>
/// <param name="Height">A Comment's height; <c>null</c> for every other type, which share one.</param>
/// <param name="Range">The lines this entry occupies, which is what an edit rewrites and nothing else.</param>
public sealed record FdgElement(
    string Id,
    string Type,
    string Name,
    string Description,
    string Text,
    double X,
    double Y,
    double Width,
    double? Height,
    LineRange Range)
{
    /// <summary>
    /// The height to draw: a Comment's own, or the height the other four share.
    /// </summary>
    /// <remarks>
    /// Read from the model rather than decided by the mapper, so the one place that knows a
    /// Comment is the type with its own height is this record and not every caller.
    /// </remarks>
    public double DrawnHeight => Height ?? FdgGeometry.SharedHeight;

    /// <summary>Whether this entry declares itself a Comment, which is what makes `height` and `text` its own.</summary>
    public bool IsComment => string.Equals(Type, FdgElementTypes.Comment, StringComparison.Ordinal);
}

/// <summary>The five element types the document may declare, stated once.</summary>
/// <remarks>
/// Strings rather than an enum, because <b>the parser must keep a type it does not recognise</b>:
/// the entry survives, draws nothing, and is reported as unreadable. An enum would force the
/// parser to choose a member or throw, and both lose the author's text.
/// </remarks>
public static class FdgElementTypes
{
    public const string UiElement = "ui-element";
    public const string DataElement = "data-element";
    public const string Action = "action";
    public const string Function = "function";
    public const string Comment = "comment";

    /// <summary>The five, in the order the design lists them.</summary>
    public static readonly IReadOnlyList<string> All = [UiElement, DataElement, Action, Function, Comment];

    /// <summary>Whether the text names one of the five.</summary>
    public static bool IsKnown(string type) => All.Contains(type, StringComparer.Ordinal);
}
