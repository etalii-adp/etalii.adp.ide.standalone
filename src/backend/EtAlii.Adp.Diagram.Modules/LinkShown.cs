using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// How a linked element shows its link (DISL 4.12, etalii.adp spec 092, T018): labelled by its target's name as the workspace
/// now holds it, and marked broken when the link does not resolve, when it is labelled by the name the link cached.
/// </summary>
public static class LinkShown
{
    /// <summary>
    /// The label and whether the target resolves, for the link <paramref name="link"/> whose cached name is <paramref name="cached"/>.
    /// Without a workspace to resolve against, a link that parses counts as resolving, as nothing says otherwise.
    /// </summary>
    public static (string Label, bool Linked) Of(string? link, string cached, LinkWorkspace? workspace)
    {
        if (link is not { Length: > 0 }) return (cached, false);
        if (workspace is null) return (cached, FblLink.Parse(link) is not null);
        var resolution = workspace.Resolve(link);
        return resolution.Resolved ? (resolution.Refreshed?.Name ?? cached, true) : (cached, false);
    }
}
