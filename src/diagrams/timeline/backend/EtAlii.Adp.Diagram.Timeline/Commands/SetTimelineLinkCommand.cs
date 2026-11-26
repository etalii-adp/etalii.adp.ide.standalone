using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// Sets a linked element's link, or removes it, as the fixes of its finding do (FBL 17.6, etalii.adp spec 092, T017).
/// </summary>
/// <param name="BodyPath">The document.</param>
/// <param name="ElementId">Which element.</param>
/// <param name="Link">The link's new text, or null to remove it, which leaves a plain element named by <paramref name="Label"/>.</param>
/// <param name="Label">The label a plain element is given when its link is removed: the name the link cached.</param>
public sealed record SetTimelineLinkCommand(
    string BodyPath,
    string ElementId,
    string? Link,
    string Label) : IDocumentEditCommand;
