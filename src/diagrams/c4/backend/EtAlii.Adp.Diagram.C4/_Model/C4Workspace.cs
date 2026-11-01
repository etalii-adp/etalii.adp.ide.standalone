namespace EtAlii.Adp.Diagram.C4;

/// <summary>What a body reads to through the binding (<see cref="C4Body"/>): its model, its relationships, its views and its styles.</summary>
/// <param name="Name">The workspace name, for a title that wants to say it.</param>
/// <param name="Elements">Every declared element, in document order.</param>
/// <param name="Relationships">Every declared relationship, in document order.</param>
/// <param name="Views">Every declared view, in document order - the first is what a bare document opens.</param>
/// <param name="Styles">Tag-based element styles, which override ADP's default theme.</param>
/// <param name="Includes">
/// The files this document pulls in with <c>!include</c>. ADP does not follow them, so whatever
/// they declare is missing from the model - which the rule set reports, rather than leaving the
/// user to wonder where half their diagram went.
/// </param>
public sealed record C4Workspace(
    string Name,
    IReadOnlyList<C4Element> Elements,
    IReadOnlyList<C4Relationship> Relationships,
    IReadOnlyList<C4View> Views,
    IReadOnlyList<C4ElementStyle> Styles,
    IReadOnlyList<string> Includes)
{
    public static C4Workspace Empty { get; } = new("", [], [], [], [], []);

    /// <summary>The element with this id, or null. Ids are case-insensitive in the DSL.</summary>
    public C4Element? Find(string id) =>
        Elements.FirstOrDefault(element => string.Equals(element.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>The view with this key, or null.</summary>
    public C4View? FindView(string key) =>
        Views.FirstOrDefault(view => string.Equals(view.Key, key, StringComparison.OrdinalIgnoreCase));
}
