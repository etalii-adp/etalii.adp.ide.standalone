using EtAlii.Adp.Context;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Fbl.Links;
using ContextScope = EtAlii.Adp.Documents.Wire.ContextScope;

namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// What a selected row of a knowledge table offers (knowledge.md; FBL 17.5, etalii.adp spec 092): "Linked from (N)" where links of
/// the workspace point at the row, which lists them and opens the one picked, as a diagram's element offers it. The diagram
/// modules' own is not reachable from a designer type, so the same three steps are taken here over the workspace's backlinks.
/// </summary>
internal sealed class KnowledgeRowActionProvider(ILinkIndex? links = null) : IContextActionProvider
{
    /// <summary>List the links that point at a row, and open the one picked.</summary>
    public const string LinkedFrom = "knowledge.linked-from";

    /// <summary>Between a backlink's document and the element that holds it, in a choice's option id: neither a path nor an id holds it.</summary>
    private const char Separator = '\n';

    private const string NoneAnyMore = "No link of the workspace points at this row any more.";

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ContextActionGroupDefinition> groups = BacklinksOf(target) is { Count: > 0 } backlinks
            ? [new ContextActionGroupDefinition([new ContextActionDefinition(LinkedFrom, $"Linked from ({backlinks.Count})…", "mdi-link-variant")])]
            : [];
        return ValueTask.FromResult(groups);
    }

    public ValueTask<ContextExecutionResult> ExecuteAsync(ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (actionId != LinkedFrom)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed($"Unknown action '{actionId}'."));
        }

        if (WorkspaceOf(target) is not { } workspace || BacklinksOf(target) is not { Count: > 0 } backlinks)
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed(NoneAnyMore));
        }

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresChoice(new ContextChoiceRequest(
            $"Linked from ({backlinks.Count})",
            "mdi-link-variant",
            "Open",
            [.. backlinks.Select(backlink => new ContextOptionNode(backlink.Path + Separator + (backlink.Element ?? backlink.Holder), LabelOf(backlink, workspace), Selectable: true, Icon: "mdi-link-variant"))],
            NoneAnyMore)));
    }

    public ValueTask<ContextValidationResult> ValidateAsync(ContextTarget target, string actionId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextValidationResult.Accepted);

    public ValueTask<ContextCommitResult> CommitAsync(ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(value);
        var at = value.IndexOf(Separator, StringComparison.Ordinal);
        return ValueTask.FromResult(actionId != LinkedFrom || at <= 0
            ? ContextCommitResult.Failed("That link is not in the workspace any more.")
            : new ContextCommitResult(true, Next: new ContextExecutionReveals(Path.GetFullPath(value[..at]), value[(at + 1)..])));
    }

    /// <summary>The workspace of a selected row of this designer type's; null for anything else, or where the index holds none.</summary>
    private LinkWorkspace? WorkspaceOf(ContextTarget target) =>
        links is not null && target.Origin == KnowledgeRowSourceResolver.RowOrigin && target.ElementId.Length > 0 ? links.WorkspaceOf(target.ResolvedFullPath) : null;

    /// <summary>The links of the workspace that point at the selected row.</summary>
    private IReadOnlyList<Backlink> BacklinksOf(ContextTarget target) =>
        WorkspaceOf(target) is { } workspace ? workspace.Backlinks(Path.GetFullPath(target.ResolvedFullPath).Replace('\\', '/'), target.ElementId) : [];

    /// <summary>How a backlink is listed: the name of the element that holds it, else the name it shows, else its id; and its document.</summary>
    private static string LabelOf(Backlink backlink, LinkWorkspace workspace) =>
        $"{(workspace.NameOf(backlink.Path, backlink.Element ?? backlink.Holder) is { Length: > 0 } name ? name : backlink.TargetName is { Length: > 0 } shown ? shown : backlink.Holder)} ({workspace.Display(backlink.Path)})";
}
