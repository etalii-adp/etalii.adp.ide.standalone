using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using EtAlii.Adp.Links;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The fixes of a linked element's finding (<see cref="DislLinkFixes"/>) as context actions, for any diagram module: offered
/// on the element's menu, run through the project's history, and, for an ambiguous link's choice, the other candidates given
/// new document ids through the index (FBL 17.6, etalii.adp spec 092, T017).
/// </summary>
/// <remarks>
/// What a fix writes into the consuming document is the module's, given as <c>relink</c>: the command that sets the
/// element's link to a text, or, given null, removes it and names the element by the name it cached.
/// </remarks>
public sealed class LinkFixActions(IHistoryStackStore historyStacks, ILinkIndex? links)
{
    /// <summary>The finding of the target element's link, when one stands.</summary>
    public LinkFix? FixOf(DislDiagram diagram, ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return DislLinkFixes.Of(diagram, target.ElementId, links?.WorkspaceOf(target.ResolvedFullPath));
    }

    /// <summary>The fixes the target element's menu offers; empty when its link needs none.</summary>
    public IReadOnlyList<ContextActionGroupDefinition> Menu(DislDiagram diagram, ContextTarget target) =>
        DislLinkFixes.Menu(FixOf(diagram, target));

    /// <summary>Runs the fix <paramref name="actionId"/>: removes the link at once, or asks which candidate an ambiguous one means.</summary>
    public async ValueTask<ContextExecutionResult> ExecuteAsync(
        DislDiagram diagram,
        ContextTarget target,
        string actionId,
        Func<LinkFix, string?, ICommand?> relink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relink);
        var fix = FixOf(diagram, target);
        switch (actionId)
        {
            case DislLinkFixes.RemoveActionId when fix is not null && fix.Finding.Code != LinkCodes.AmbiguousDocument:
                if (relink(fix, null) is not { } remove) return new ContextExecutionFailed("This link cannot be removed here.");
                var result = await historyStacks.Get(target.Root).ExecuteAsync(remove, cancellationToken);
                return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);

            case DislLinkFixes.ChooseActionId when fix is not null && fix.Finding.Candidates.Count > 1 && links?.WorkspaceOf(target.ResolvedFullPath) is { } workspace:
                return DislLinkFixes.Choice(fix, workspace);

            default:
                return new ContextExecutionFailed("This link no longer needs fixing.");
        }
    }

    /// <summary>
    /// Commits the choice of <paramref name="path"/> for an ambiguous link: every other candidate is given a new document id, and
    /// the link, when it named the chosen document otherwise, is rewritten to name it, as one step of the history.
    /// </summary>
    public async ValueTask<ContextCommitResult> ChooseAsync(
        DislDiagram diagram,
        ContextTarget target,
        string path,
        Func<LinkFix, string?, ICommand?> relink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(relink);
        if (links is null || FixOf(diagram, target) is not { } fix || links.WorkspaceOf(target.ResolvedFullPath) is not { } workspace)
        {
            return ContextCommitResult.Failed("This link no longer needs fixing.");
        }

        if (DislLinkFixes.Chosen(fix, workspace, path) is not { } chosen)
        {
            return ContextCommitResult.Failed("That document is no longer one the link could mean.");
        }

        if (await links.RenewAsync(chosen.Renewed, cancellationToken) is { } refusal)
        {
            return ContextCommitResult.Failed(refusal);
        }

        if (chosen.Text == fix.Text) return ContextCommitResult.Succeeded;
        if (relink(fix, chosen.Text) is not { } command) return ContextCommitResult.Failed("This link cannot be changed here.");
        var result = await historyStacks.Get(target.Root).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextCommitResult.Succeeded : ContextCommitResult.Failed(result.Error);
    }
}
