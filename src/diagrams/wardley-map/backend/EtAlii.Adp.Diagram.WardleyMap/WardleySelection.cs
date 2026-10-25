using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// What an element id the canvas sends selects, beyond an element of the map: the one vocabulary the
/// resolver and the action provider share, so a right-click and the menu it opens address the same thing.
/// </summary>
/// <remarks>
/// An element's id is the store's identity for it, kept beside the map, so it needs no reading here.
/// <c>new:{x},{y}</c> is not an element at all - it is where the user asked for something on empty map,
/// built and parsed by the shared <see cref="GestureIds"/> grammar - and its subject is the map itself,
/// which the definition's menus and steps address by the empty id.
/// </remarks>
public static class WardleySelection
{
    /// <summary>What a placement reads as: the map, which is its subject.</summary>
    public const string ThisMap = "This map";

    /// <summary>Whether an id is a placement on empty map rather than an element.</summary>
    public static bool IsPlacement(string? elementId) => GestureIds.TryParsePlacement(elementId, out _, out _);

    /// <summary>The id the definition is asked about for <paramref name="elementId"/>: the empty id, the map, for a placement; the id itself otherwise.</summary>
    public static string SubjectOf(string elementId) => IsPlacement(elementId) ? "" : elementId;
}
