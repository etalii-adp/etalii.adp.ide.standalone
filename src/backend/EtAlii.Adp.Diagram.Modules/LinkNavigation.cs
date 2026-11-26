using EtAlii.Adp.Context;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// Going along a link, for any diagram module (DISL 4.12, FBL 17.5, etalii.adp spec 092): "Open linked element" on a linked
/// element whose link resolves, which opens the target's document with the target selected (FR-014), and "Linked from (N)" on an
/// element that links of the workspace point at, which lists them and opens the one picked (FR-015).
/// </summary>
public static class LinkNavigation
{
    /// <summary>Open the element a linked element stands for.</summary>
    public const string OpenActionId = "link.open";

    /// <summary>List the links that point at an element, and open the one picked.</summary>
    public const string LinkedFromActionId = "link.linked-from";

    /// <summary>Between a backlink's document and the element that holds it, in a choice's option id: neither a path nor an id holds it.</summary>
    private const char Separator = '\n';

    /// <summary>Whether <paramref name="actionId"/> is one of the two.</summary>
    public static bool Handles(string actionId) => actionId is OpenActionId or LinkedFromActionId;

    /// <summary>What the element of <paramref name="target"/> offers of the two: each only where it leads somewhere.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menu(DislDiagram diagram, ContextTarget target, LinkWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (workspace is null || ElementOf(diagram, target.ElementId) is not { } element) return [];

        var entries = new List<ContextActionDefinition>();
        if (Target(element, workspace) is not null) entries.Add(new ContextActionDefinition(OpenActionId, "Open linked element", "mdi-open-in-new"));
        if (BacklinksOf(element, target, workspace) is { Count: > 0 } backlinks) entries.Add(new ContextActionDefinition(LinkedFromActionId, $"Linked from ({backlinks.Count})…", "mdi-link-variant"));
        return entries.Count == 0 ? [] : [new ContextActionGroupDefinition(entries)];
    }

    /// <summary>Runs <paramref name="actionId"/> on the element of <paramref name="target"/>: where it leads, the list to pick from, or why it leads nowhere now.</summary>
    public static ContextExecutionResult Execute(DislDiagram diagram, ContextTarget target, string actionId, LinkWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (workspace is null || ElementOf(diagram, target.ElementId) is not { } element) return new ContextExecutionFailed("That is no longer in this diagram.");

        if (actionId == OpenActionId)
        {
            return (ContextExecutionResult?)Target(element, workspace) ?? new ContextExecutionFailed("The linked element is not in the workspace any more.");
        }

        var backlinks = BacklinksOf(element, target, workspace);
        if (backlinks.Count == 0) return new ContextExecutionFailed("No link of the workspace points at this element any more.");
        return new ContextExecutionRequiresChoice(new ContextChoiceRequest(
            $"Linked from ({backlinks.Count})",
            "mdi-link-variant",
            "Open",
            [.. backlinks.Select(backlink => new ContextOptionNode(backlink.Path + Separator + backlink.Holder, LabelOf(backlink, workspace), Selectable: true, Icon: "mdi-link-variant"))],
            "No link of the workspace points at this element any more."));
    }

    /// <summary>The backlink <paramref name="value"/> names, picked from "Linked from (N)": where it leads, as the answer's next step.</summary>
    public static ContextCommitResult Picked(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var at = value.IndexOf(Separator, StringComparison.Ordinal);
        return at <= 0
            ? ContextCommitResult.Failed("That link is not in the workspace any more.")
            : new ContextCommitResult(true, Next: new ContextExecutionReveals(FullPathOf(value[..at]), value[(at + 1)..]));
    }

    /// <summary>
    /// The confirmation deleting the element of <paramref name="target"/> asks for, once the links of the workspace that the delete
    /// breaks are counted (FR-016): <paramref name="confirmation"/> as it is when none break; with the count added to its message
    /// when some do; and, where nothing was to be asked, a confirmation of its own saying how many, since a delete that breaks links
    /// is not done without saying so. The element's nodes beneath it count too, because they go with it.
    /// </summary>
    public static ContextConfirmationRequest? WarnOfBrokenLinks(ContextConfirmationRequest? confirmation, DislDiagram diagram, ContextTarget target, LinkWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (workspace is null || ElementOf(diagram, target.ElementId) is not { } element) return confirmation;

        var removed = Removed(element).Where(candidate => candidate.Type.Linkable).Select(DislLinkFixes.WrittenIdOf).Where(id => id.Length > 0);
        var broken = workspace.BrokenByDeleting(Path.GetFullPath(target.ResolvedFullPath).Replace('\\', '/'), removed);
        if (broken == 0) return confirmation;

        var warning = broken == 1 ? "This breaks 1 link from another document." : $"This breaks {broken} links from other documents.";
        return confirmation is null
            ? new ContextConfirmationRequest("Delete", "mdi-delete-outline", warning, "Delete", Danger: true)
            : confirmation with { Message = confirmation.Message.Length == 0 ? warning : $"{confirmation.Message} {warning}" };
    }

    /// <summary>An element and every node beneath it.</summary>
    private static IEnumerable<DislElement> Removed(DislElement element) => [element, .. element.Children.SelectMany(Removed)];

    /// <summary>Where a linked element's link leads, when it resolves to an element; null otherwise.</summary>
    private static ContextExecutionReveals? Target(DislElement element, LinkWorkspace workspace) =>
        element.Type.LinkedAttribute is { } attribute && element.ValueOf(attribute) is string { Length: > 0 } text &&
        workspace.Resolve(text) is { Resolved: true, Document: { } document, Element: { } linked }
            ? new ContextExecutionReveals(FullPathOf(document.Path), linked.Id)
            : null;

    /// <summary>The links of the workspace that point at a linkable element, by the id the file writes for it.</summary>
    private static IReadOnlyList<Backlink> BacklinksOf(DislElement element, ContextTarget target, LinkWorkspace workspace) =>
        element.Type.Linkable ? workspace.Backlinks(Path.GetFullPath(target.ResolvedFullPath).Replace('\\', '/'), DislLinkFixes.WrittenIdOf(element)) : [];

    /// <summary>
    /// How a backlink is listed: the name of the element that holds it, where the index knows one, else the name it shows, which is its
    /// target's, as a linked element has none of its own; its id only when neither is known; and its document.
    /// </summary>
    private static string LabelOf(Backlink backlink, LinkWorkspace workspace) =>
        $"{(workspace.NameOf(backlink.Path, backlink.Holder) is { Length: > 0 } name ? name : backlink.TargetName is { Length: > 0 } shown ? shown : backlink.Holder)} ({workspace.Display(backlink.Path)})";

    /// <summary>The element <paramref name="elementId"/> names, by its wire id or the id its file writes; null for none.</summary>
    private static DislElement? ElementOf(DislDiagram diagram, string elementId)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.Elements.FirstOrDefault(candidate => !candidate.IsDerived && (candidate.Id == elementId || DislLinkFixes.WrittenIdOf(candidate) == elementId));
    }

    /// <summary>An index path, with forward slashes, as the platform writes it.</summary>
    private static string FullPathOf(string indexPath) => Path.GetFullPath(indexPath);
}
