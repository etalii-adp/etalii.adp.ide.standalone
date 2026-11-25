using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Sets a linked node's link, or removes it, as the fixes of its finding do (FBL 17.6, etalii.adp spec 092, T017).
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementId">Which node, by its written id.</param>
/// <param name="Link">The link's new text, or null to remove it, which leaves an ordinary node named by <paramref name="Label"/>.</param>
/// <param name="Label">The label an ordinary node is given when its link is removed: the name the link cached.</param>
public sealed record SetDependencyGraphLinkCommand(
    string BodyPath,
    string ElementId,
    string? Link,
    string Label) : IDocumentEditCommand;
