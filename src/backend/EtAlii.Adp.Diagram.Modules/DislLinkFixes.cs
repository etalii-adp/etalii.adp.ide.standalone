using EtAlii.Adp.Context;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The fixes FBL 17.6 names for a linked element whose link does not resolve (etalii.adp spec 092, T017), offered on the
/// element's context menu, which selecting its finding in the problems list reaches: remove the link of a malformed or
/// broken one, and choose the target of an ambiguous one among its candidates.
/// </summary>
/// <remarks>
/// What a fix writes is the module's (<see cref="DiagramActionProvider{TBody, TModel}.Relink"/>): the link set to
/// another text, or removed, which leaves a plain element named by the name the link cached. Giving the other
/// candidates new document ids is the index's (<see cref="Links.ILinkIndex.RenewAsync"/>).
/// </remarks>
public static class DislLinkFixes
{
    /// <summary>Remove the link: the element stays where it is, named by the name the link cached.</summary>
    public const string RemoveActionId = "link.remove";

    /// <summary>Choose which of an ambiguous link's candidates it means; the others are given new document ids.</summary>
    public const string ChooseActionId = "link.choose";

    private const string StoredId = "storedId";

    /// <summary>Whether <paramref name="actionId"/> is one of the fixes.</summary>
    public static bool IsFix(string actionId) => actionId is RemoveActionId or ChooseActionId;

    /// <summary>
    /// The link of the element <paramref name="elementId"/> (its wire id or its written id) of <paramref name="diagram"/>
    /// when its finding stands, as <see cref="DislLinkFindings"/> reports it; null when it has none or resolves.
    /// </summary>
    public static LinkFix? Of(DislDiagram diagram, string elementId, LinkWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var element = diagram.Elements.FirstOrDefault(candidate => !candidate.IsDerived && (candidate.Id == elementId || WrittenIdOf(candidate) == elementId));
        if (element?.Type.LinkedAttribute is not { } attribute || element.ValueOf(attribute) is not string { Length: > 0 } text) return null;
        return DislLinkFindings.FindingOf(text, workspace) is { } finding ? new LinkFix(text, finding) : null;
    }

    /// <summary>The group of fixes on the element's menu; empty when nothing is to be fixed.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menu(LinkFix? fix)
    {
        if (fix is null) return [];
        if (fix.Finding.Code != LinkCodes.AmbiguousDocument)
        {
            return [new ContextActionGroupDefinition([new ContextActionDefinition(RemoveActionId, "Remove the link", "mdi-link-variant-off")])];
        }

        // A duplicate element id in one target has one candidate, and choosing it would change nothing.
        return fix.Finding.Candidates.Count > 1
            ? [new ContextActionGroupDefinition([new ContextActionDefinition(ChooseActionId, "Choose the linked document…", "mdi-link-variant")])]
            : [];
    }

    /// <summary>The candidates of an ambiguous link, to choose from by path.</summary>
    public static ContextExecutionResult Choice(LinkFix fix, LinkWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(workspace);
        return new ContextExecutionRequiresChoice(new ContextChoiceRequest(
            "Choose the linked document",
            "mdi-link-variant",
            "Choose",
            [.. fix.Finding.Candidates.Select(path => new ContextOptionNode(path, workspace.Display(path), Selectable: true, Icon: "mdi-file-outline"))],
            "No document is left to choose from."));
    }

    /// <summary>
    /// What choosing <paramref name="path"/> does: the link as it then reads, naming the chosen document by its id and
    /// name, and the other candidates, to be given new document ids. Null when <paramref name="path"/> is no candidate.
    /// </summary>
    public static LinkChoice? Chosen(LinkFix fix, LinkWorkspace workspace, string path)
    {
        ArgumentNullException.ThrowIfNull(fix);
        ArgumentNullException.ThrowIfNull(workspace);
        if (!fix.Finding.Candidates.Contains(path, StringComparer.Ordinal) || workspace.Document(path) is not { } chosen) return null;
        var link = FblLink.Parse(fix.Text)!;
        var text = (link with { DocumentId = chosen.DocumentId ?? link.DocumentId, DocumentName = chosen.FileName }).Text;
        return new LinkChoice(text, [.. fix.Finding.Candidates.Where(candidate => candidate != path)]);
    }

    internal static string WrittenIdOf(DislElement element) =>
        element.HostAttributes.TryGetValue(StoredId, out var id) && id is string { Length: > 0 } written ? written : element.Id;
}

/// <summary>The link of a linked element whose link has a finding: its text and the finding.</summary>
public sealed record LinkFix(string Text, LinkFinding Finding)
{
    /// <summary>The name the link caches, which names the element once the link is removed; empty when it caches none.</summary>
    public string CachedName => FblLink.Parse(Text)?.Name ?? "";
}

/// <summary>A chosen candidate: the link's text naming it, and the other candidates, which are given new document ids.</summary>
public sealed record LinkChoice(string Text, IReadOnlyList<string> Renewed);
