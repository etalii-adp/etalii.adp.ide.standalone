using System.Globalization;
using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// What a user can do to a causal loop diagram, offered through the standard provider path so the
/// ribbon, the right-click menu, the keyboard and a toolbox drop all reach the same command
/// (causal-loop-diagram Requirements 5.3, 5.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Every gesture is discovered unavailable with its reason rather than silently absent.</b> A
/// missing menu entry tells a user nothing; an entry greyed out with a sentence tells them what
/// to change. The one exception is a gesture that makes no sense for the selection at all - there
/// is no "set polarity" on a variable, because polarity is not a thing a variable has.
/// </para>
/// <para>
/// <b>What is offered depends only on what is selected.</b> A variable offers rename, remove and
/// a new link from it; a link offers its polarity, its delay, which way its arc bows and
/// removal; a loop offers rename
/// and removal; and a placement offers a new variable. The provider reads the document to fill in
/// the counts and the current values, and never to decide whether the user is allowed.
/// </para>
/// </remarks>
public sealed class CausalLoopContextActionProvider(
    ICausalLoopDocumentStore documents,
    IHistoryStackStore historyStacks,
    IDiagramViewportRegistry sessions) : IContextActionProvider
{
    /// <summary>Declare a new variable at the point the user asked for one.</summary>
    public const string AddVariableActionId = "causal-loop.add-variable";

    /// <summary>Rename a variable, carrying every link and loop that names it.</summary>
    public const string RenameVariableActionId = "causal-loop.rename-variable";

    /// <summary>Remove a variable, and the links and loops that named it.</summary>
    public const string RemoveVariableActionId = "causal-loop.remove-variable";

    /// <summary>State a link from the selected variable to another.</summary>
    public const string AddLinkActionId = "causal-loop.add-link";

    /// <summary>State a link drawn from one variable to another - the two ends carried in the id, no dialog.</summary>
    public const string ConnectActionId = "causal-loop.connect";

    /// <summary>Say the effect runs the same way.</summary>
    public const string MakePositiveActionId = "causal-loop.make-positive";

    /// <summary>Say the effect runs the opposite way.</summary>
    public const string MakeNegativeActionId = "causal-loop.make-negative";

    /// <summary>Mark or unmark a link's effect as delayed.</summary>
    public const string ToggleDelayActionId = "causal-loop.toggle-delay";

    /// <summary>Bow the link's arc to the other side of its chord.</summary>
    /// <remarks>
    /// Which side an arc bows to is this module's own decision, taken from the direction of
    /// travel so that a two-variable loop draws as an ellipse. That is right almost always and
    /// occasionally unreadable, where the chosen side crosses another link. This overrides it for
    /// one link, and the choice is stated in the document so it survives a reopen.
    /// </remarks>
    public const string FlipCurvatureActionId = "causal-loop.flip-curvature";

    /// <summary>Withdraw a link. Loops through it survive.</summary>
    public const string RemoveLinkActionId = "causal-loop.remove-link";

    /// <summary>Claim a loop through the selected variable and another.</summary>
    public const string AddLoopActionId = "causal-loop.add-loop";

    /// <summary>Rename a loop, without touching what it runs through.</summary>
    public const string RenameLoopActionId = "causal-loop.rename-loop";

    /// <summary>Withdraw a loop's claim. Its links survive.</summary>
    public const string RemoveLoopActionId = "causal-loop.remove-loop";

    /// <summary>
    /// Arrange the whole diagram with the self-organizing layout (Requirement 6.1).
    /// </summary>
    /// <remarks>
    /// Diagram-wide, and offered where the diagram itself is the selection: on the canvas with
    /// nothing picked. It is invoked, never automatic - a document opens with the ring layout and
    /// stays where the author left it until somebody asks for this.
    /// </remarks>
    public const string ArrangeActionId = "causal-loop.arrange";

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextActionGroupDefinition>> DiscoverAsync(
        ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AnswersFor(target))
        {
            return Groups([]);
        }

        var entry = documents.GetOrLoad(target.ResolvedFullPath);
        if (!entry.IsUsable)
        {
            return Groups([]);
        }

        // A relation gesture is a link the user drew; the only thing offered on it is stating that
        // link, which is what the gesture asked for. Discovered so ExecuteAction accepts it.
        if (CausalLoopSelection.RelationOf(target.ElementId) is not null)
        {
            return Groups([new ContextActionGroupDefinition(
                [new ContextActionDefinition(ConnectActionId, "Add link", "mdi-arrow-right-thin")])]);
        }

        if (CausalLoopSelection.IsPlacement(target.ElementId))
        {
            var arrangeable = entry.Model.Variables.Count > 1;

            return Groups([new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(AddVariableActionId, "Add variable", "mdi-plus-circle-outline"),
                new ContextActionDefinition(
                    ArrangeActionId, "Arrange diagram", "mdi-graph-outline", null,
                    arrangeable,
                    "There is nothing to arrange until this diagram has two variables."),
            ])]);
        }

        if (CausalLoopSelection.VariableOf(target.ElementId) is { } variable)
        {
            return Groups(VariableActions(entry.Model, variable));
        }

        if (CausalLoopSelection.LinkOf(target.ElementId) is { } ends)
        {
            return Groups(LinkActions(entry.Model, ends.From, ends.To));
        }

        if (CausalLoopSelection.LoopOf(target.ElementId) is { } loop)
        {
            return Groups(LoopActions(entry.Model, loop));
        }

        return Groups([]);
    }

    /// <inheritdoc />
    public ValueTask<ContextExecutionResult> ExecuteAsync(
        ContextTarget target, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AnswersFor(target) || !actionId.StartsWith("causal-loop.", StringComparison.Ordinal))
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed("That action is not this diagram's."));
        }

        var entry = documents.GetOrLoad(target.ResolvedFullPath);

        // The gestures that need a word from the user ask for one; the rest run.
        return actionId switch
        {
            AddVariableActionId => AddVariableAsync(target, cancellationToken),
            RenameVariableActionId => AskInline(
                "Rename variable", "mdi-rename-outline", "Name",
                LabelOf(entry.Model, target.ElementId), target.ElementId),
            AddLinkActionId => Ask("Add link", "mdi-arrow-right-thin", "To variable", ""),
            AddLoopActionId => Ask("Claim a loop", "mdi-sync", "Identifier", NextLoopIdentifier(entry.Model)),
            RenameLoopActionId => Ask(
                "Rename loop", "mdi-rename-outline", "Name",
                entry.Model.Loops.FirstOrDefault(loop => loop.Id == target.ElementId)?.Name ?? ""),
            RemoveVariableActionId => Confirm(entry.Model, target),
            ArrangeActionId => ArrangeAsync(target, cancellationToken),
            _ => RunAsync(target, actionId, "", cancellationToken),
        };
    }

    /// <inheritdoc />
    public ValueTask<ContextValidationResult> ValidateAsync(
        ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!AnswersFor(target))
        {
            return ValueTask.FromResult(ContextValidationResult.Accepted);
        }

        var entry = documents.GetOrLoad(target.ResolvedFullPath);

        return ValueTask.FromResult(actionId switch
        {
            // The label is written quoted, so it may hold spaces but not a quote or a line break,
            // and it must not be empty - an empty name leaves nothing to read on the variable.
            RenameVariableActionId when value.Length == 0 || value.Contains('"', StringComparison.Ordinal) || value.Any(char.IsControl) =>
                ContextValidationResult.Rejected("A name needs at least one character, and cannot contain a quote or a line break."),
            AddLinkActionId when !entry.Model.Declares(value) =>
                ContextValidationResult.Rejected($"'{value}' is not a variable in this diagram."),
            AddLoopActionId when entry.Model.Loops.Any(loop => loop.Identifier == value) =>
                ContextValidationResult.Rejected("A loop of that identifier is already stated in this diagram."),
            _ => ContextValidationResult.Accepted,
        });
    }

    /// <inheritdoc />
    public async ValueTask<ContextCommitResult> CommitAsync(
        ContextTarget target, string actionId, string value, string text, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        _ = text;

        var result = await RunAsync(target, actionId, value, cancellationToken);
        return result is ContextExecutionFailed failed
            ? ContextCommitResult.Failed(failed.Message)
            : ContextCommitResult.Succeeded;
    }

    /// <summary>The command one action-and-value pair dispatches, or null when it is not ours.</summary>
    internal ICommand? CommandFor(ContextTarget target, string actionId, string value)
    {
        var body = target.ResolvedFullPath;
        var entry = documents.GetOrLoad(body);
        var variable = CausalLoopSelection.VariableOf(target.ElementId);
        var link = CausalLoopSelection.LinkOf(target.ElementId);
        var loop = CausalLoopSelection.LoopOf(target.ElementId);

        return actionId switch
        {
            AddVariableActionId => new AddVariableCommand(body, value, value),
            // The visible name is the label, so renaming edits the label - the identifier a link
            // refers to stays put. It is edited inline, over the variable, rather than in a dialog.
            RenameVariableActionId when variable is not null => new SetVariableLabelCommand(body, variable, value),
            RemoveVariableActionId when variable is not null => new RemoveVariableCommand(body, variable),
            AddLinkActionId when variable is not null =>
                new AddLinkCommand(body, variable, value, CausalLoopPolarity.Positive),
            ConnectActionId when CausalLoopSelection.RelationOf(target.ElementId) is { } ends =>
                new AddLinkCommand(body, ends.From, ends.To, CausalLoopPolarity.Positive),
            AddLoopActionId when variable is not null =>
                new AddLoopCommand(body, value, value, LoopThrough(entry.Model, variable)),
            MakePositiveActionId when link is not null =>
                new SetLinkPolarityCommand(body, link.Value.From, link.Value.To, CausalLoopPolarity.Positive),
            MakeNegativeActionId when link is not null =>
                new SetLinkPolarityCommand(body, link.Value.From, link.Value.To, CausalLoopPolarity.Negative),
            ToggleDelayActionId when link is not null =>
                new SetLinkDelayCommand(body, link.Value.From, link.Value.To, !IsDelayed(entry.Model, link.Value)),
            FlipCurvatureActionId when link is not null =>
                new SetLinkCurvatureCommand(body, link.Value.From, link.Value.To, !IsFlipped(entry.Model, link.Value)),
            RemoveLinkActionId when link is not null =>
                new RemoveLinkCommand(body, link.Value.From, link.Value.To),
            RenameLoopActionId when loop is not null => new SetLoopNameCommand(body, loop, value),
            RemoveLoopActionId when loop is not null => new RemoveLoopCommand(body, loop),
            _ => null,
        };
    }

    private static bool AnswersFor(ContextTarget target) => Diagram.IsBody(target.ResolvedFullPath);

    private static ValueTask<IReadOnlyList<ContextActionGroupDefinition>> Groups(
        IReadOnlyList<ContextActionGroupDefinition> groups) =>
        ValueTask.FromResult(groups);

    private static ValueTask<ContextExecutionResult> Ask(string title, string icon, string field, string initial) =>
        ValueTask.FromResult<ContextExecutionResult>(
            new ContextExecutionRequiresInput(new ContextInputRequest(title, icon, field, initial, "Apply")));

    /// <summary>
    /// An inline prompt: the same input, marked to open over the element whose visible text it is
    /// rather than in a dialog. A rename of a variable edits the name a reader sees on it.
    /// </summary>
    private static ValueTask<ContextExecutionResult> AskInline(string title, string icon, string field, string initial, string elementId) =>
        ValueTask.FromResult<ContextExecutionResult>(
            new ContextExecutionRequiresInput(new ContextInputRequest(title, icon, field, initial, "Apply", elementId)));

    /// <summary>The visible name a variable id names, for seeding the inline rename with what is there.</summary>
    private static string LabelOf(CausalLoopModel model, string? elementId) =>
        CausalLoopSelection.VariableOf(elementId) is { } id
            ? model.Variables.FirstOrDefault(variable => variable.Id == id)?.Display ?? ""
            : "";

    /// <summary>
    /// Adds a variable nobody named, at the point it was dropped or right-clicked. The name is the
    /// module's own next free one, so no dialog interrupts the gesture; the position is authored
    /// through the session where there is a registration to write it into, and left to the layout
    /// where there is not.
    /// </summary>
    private async ValueTask<ContextExecutionResult> AddVariableAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        var entry = documents.GetOrLoad(target.ResolvedFullPath);
        var (id, label) = CausalLoopWriter.NextVariableName(entry.Model);

        if (CausalLoopSelection.PlacementPoint(target.ElementId) is { } point &&
            sessions.Find(target.WatchId, target.ResolvedFullPath) is CausalLoopSession session)
        {
            var refusal = await session.AddVariableAtAsync(id, label, point.X, point.Y, cancellationToken);
            return refusal.Length == 0 ? new ContextExecutionCompleted() : new ContextExecutionFailed(refusal);
        }

        var result = await historyStacks.Get(target.RootPath).ExecuteAsync(
            new AddVariableCommand(target.ResolvedFullPath, id, label), cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    /// <summary>
    /// Removing a variable takes the links and loops that named it, so the count is stated before
    /// anything runs rather than discovered afterwards.
    /// </summary>
    private static ValueTask<ContextExecutionResult> Confirm(CausalLoopModel model, ContextTarget target)
    {
        var variable = CausalLoopSelection.VariableOf(target.ElementId) ?? "";
        var count = CausalLoopWriter.CountVariableRemoval(model, variable);

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresConfirmation(
            new ContextConfirmationRequest(
                "Remove variable",
                "mdi-delete-outline",
                count > 1
                    ? $"Removing '{variable}' also removes the {count - 1} links and loops that name it."
                    : $"Remove '{variable}'?",
                "Remove",
                Danger: true)));
    }

    private async ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target, string actionId, string value, CancellationToken cancellationToken)
    {
        if (CommandFor(target, actionId, value) is not { } command)
        {
            return new ContextExecutionFailed("That action does not apply to this selection.");
        }

        var result = await historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    /// <summary>
    /// Runs the arrangement on the session the reader has open.
    /// </summary>
    /// <remarks>
    /// The session is where the registration path lives - a context target carries the body it
    /// was opened for, and a body does not know which <c>.adp</c> registered it. Reaching the
    /// session through the viewport registry is the seam a MoveElement already uses for exactly
    /// this reason, so the arrangement travels the path a drag travels rather than a new one.
    /// </remarks>
    private async ValueTask<ContextExecutionResult> ArrangeAsync(
        ContextTarget target, CancellationToken cancellationToken)
    {
        if (sessions.Find(target.WatchId, target.ResolvedFullPath) is not CausalLoopSession session)
        {
            return new ContextExecutionFailed(
                "This diagram is not open on this connection, so there is nothing to arrange.");
        }

        var refusal = await session.ArrangeAsync(cancellationToken);
        return refusal.Length == 0 ? new ContextExecutionCompleted() : new ContextExecutionFailed(refusal);
    }

    private static IReadOnlyList<ContextActionGroupDefinition> VariableActions(CausalLoopModel model, string variable)
    {
        var known = model.Declares(variable);
        var reason = known ? "" : CausalLoopWriter.NoSuchVariable;
        var count = CausalLoopWriter.CountVariableRemoval(model, variable);

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(AddLinkActionId, "Add link from here…", "mdi-arrow-right-thin", null, known, reason),
                new ContextActionDefinition(AddLoopActionId, "Claim a loop from here…", "mdi-sync", null, known, reason),
                new ContextActionDefinition(RenameVariableActionId, "Rename…", "mdi-rename-outline", null, known, reason),
                new ContextActionDefinition(
                    RemoveVariableActionId,
                    count > 1 ? $"Remove variable (with {count - 1} references)" : "Remove variable",
                    "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"),
                    known,
                    reason),
            ]),
        ];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> LinkActions(CausalLoopModel model, string from, string to)
    {
        var link = model.Links.FirstOrDefault(candidate => candidate.From == from && candidate.To == to);
        var known = link is not null;
        var reason = known ? "" : CausalLoopWriter.NoSuchLink;

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(
                    MakePositiveActionId, "Same direction (+)", "mdi-plus", null,
                    known && link!.Polarity != CausalLoopPolarity.Positive,
                    known ? "This link already states +." : reason),
                new ContextActionDefinition(
                    MakeNegativeActionId, "Opposite direction (−)", "mdi-minus", null,
                    known && link!.Polarity != CausalLoopPolarity.Negative,
                    known ? "This link already states −." : reason),
                new ContextActionDefinition(
                    ToggleDelayActionId,
                    known && link!.Delayed ? "Not delayed" : "Delayed",
                    "mdi-timer-sand", null, known, reason),
                new ContextActionDefinition(
                    FlipCurvatureActionId,
                    known && link!.Flipped ? "Curve back the other way" : "Flip the curve",
                    "mdi-vector-curve", null, known, reason),
                new ContextActionDefinition(
                    RemoveLinkActionId, "Remove link", "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"), known, reason),
            ]),
        ];
    }

    private static IReadOnlyList<ContextActionGroupDefinition> LoopActions(CausalLoopModel model, string identifier)
    {
        var known = model.Loops.Any(loop => loop.Identifier == identifier);
        var reason = known ? "" : CausalLoopWriter.NoSuchLoop;

        return
        [
            new ContextActionGroupDefinition(
            [
                new ContextActionDefinition(RenameLoopActionId, "Rename loop…", "mdi-rename-outline", null, known, reason),
                new ContextActionDefinition(
                    RemoveLoopActionId, "Remove loop", "mdi-delete-outline",
                    new ContextShortcutDefinition("Delete"), known, reason),
            ]),
        ];
    }

    private static bool IsDelayed(CausalLoopModel model, (string From, string To) link) =>
        model.Links.FirstOrDefault(candidate => candidate.From == link.From && candidate.To == link.To)?.Delayed == true;

    private static bool IsFlipped(CausalLoopModel model, (string From, string To) link) =>
        model.Links.FirstOrDefault(candidate => candidate.From == link.From && candidate.To == link.To)?.Flipped == true;

    /// <summary>The shortest cycle through a variable, so a claimed loop starts from something real.</summary>
    private static IReadOnlyList<string> LoopThrough(CausalLoopModel model, string variable) =>
        CycleFinder.Find(model).Cycles
            .Where(cycle => cycle.Contains(variable, StringComparer.Ordinal))
            .OrderBy(cycle => cycle.Count)
            .FirstOrDefault() ?? [variable];

    /// <summary>The next free loop identifier, so a new claim does not collide with an existing one.</summary>
    private static string NextLoopIdentifier(CausalLoopModel model)
    {
        for (var number = 1; number < 1000; number++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"R{number}");
            if (!model.Loops.Any(loop => loop.Identifier == candidate))
            {
                return candidate;
            }
        }

        return "R";
    }
}
