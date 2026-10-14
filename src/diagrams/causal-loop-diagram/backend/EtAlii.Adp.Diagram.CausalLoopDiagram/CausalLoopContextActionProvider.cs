using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// What a user can do to a causal loop diagram, offered through the standard provider path so the
/// ribbon, the right-click menu, the keyboard and a toolbox drop all reach the same command
/// (causal-loop-diagram Requirements 5.3, 5.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The menus are the definition's.</b> What a selection offers, which entries are unavailable and
/// why, and what removing a variable asks first are derived from the bundled DISL definition by
/// <see cref="CausalLoopDefinition"/> (causal-loop-disl-fbl Requirement 3.1); this provider holds no
/// rule of its own about them. An id that names no element is offered nothing, as an unknown id always was.
/// </para>
/// <para>
/// <b>What an action does is still the module's command</b>, dispatched through the history stack, until
/// the body is written through the binding.
/// </para>
/// <para>
/// The scope, discovery on this module's documents, the could-not-be-read guard, the dispatch and the
/// commit leg are the shared frame's (code-structure FR-034); this says which command each action is.
/// </para>
/// </remarks>
public sealed class CausalLoopContextActionProvider(
    ICausalLoopDocumentStore documents,
    IHistoryStackStore historyStacks,
    IDiagramViewportRegistry sessions)
    : DiagramActionProvider<CausalLoopBody, CausalLoopModel>(historyStacks, documents)
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
    private const string RenameLoopActionId = "causal-loop.rename-loop";

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
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the DISL definition: the element's, the empty canvas's or the connect gesture's
    /// menu, nothing offered on a document that could not be read. Executing an action by id only
    /// finds actions its target discovers, so a drop and a finished gesture each discover theirs.
    /// </remarks>
    protected override IReadOnlyList<ContextActionGroupDefinition> Discover(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string elementId) =>
        CausalLoopDefinition.Menus(entry, elementId);

    /// <inheritdoc />
    protected override ValueTask<ContextExecutionResult> RunAsync(
        ContextTarget target, DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string actionId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(entry);

        if (!IsBody(target.ResolvedFullPath) || !actionId.StartsWith("causal-loop.", StringComparison.Ordinal))
        {
            return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed("That action is not this diagram's."));
        }

        // The gestures that need a word from the user ask for one; the rest run.
        return actionId switch
        {
            AddVariableActionId => AddVariableAsync(target, entry.Model, cancellationToken),
            RenameVariableActionId => AskInline(
                "Rename variable", "mdi-rename-outline", "Name",
                LabelOf(entry.Model, target.ElementId), target.ElementId),
            AddLinkActionId => Ask("Add link", "mdi-arrow-right-thin", "To variable", ""),
            AddLoopActionId => Ask("Claim a loop", "mdi-sync", "Identifier", NextLoopIdentifier(entry.Model)),
            RenameLoopActionId => Ask(
                "Rename loop", "mdi-rename-outline", "Name",
                entry.Model.Loops.FirstOrDefault(loop => loop.Id == target.ElementId)?.Name ?? ""),
            RemoveVariableActionId => Confirm(entry, target),
            ArrangeActionId => ArrangeAsync(target, cancellationToken),
            _ => CommitCommand(target, entry.Model, actionId, "") is { } command
                ? DispatchAsync(target, command, cancellationToken)
                : ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionFailed("That action does not apply to this selection.")),
        };
    }

    /// <inheritdoc />
    protected override ContextValidationResult Validate(ContextTarget target, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!IsBody(target.ResolvedFullPath))
        {
            return ContextValidationResult.Accepted;
        }

        var entry = Load(target);

        return actionId switch
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
        };
    }

    /// <inheritdoc />
    protected override ICommand? CommitCommand(ContextTarget target, CausalLoopModel model, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(model);

        var body = target.ResolvedFullPath;
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
                new AddLoopCommand(body, value, value, LoopThrough(model, variable)),
            MakePositiveActionId when link is not null =>
                new SetLinkPolarityCommand(body, link.Value.From, link.Value.To, CausalLoopPolarity.Positive),
            MakeNegativeActionId when link is not null =>
                new SetLinkPolarityCommand(body, link.Value.From, link.Value.To, CausalLoopPolarity.Negative),
            ToggleDelayActionId when link is not null =>
                new SetLinkDelayCommand(body, link.Value.From, link.Value.To, !IsDelayed(model, link.Value)),
            FlipCurvatureActionId when link is not null =>
                new SetLinkCurvatureCommand(body, link.Value.From, link.Value.To, !IsFlipped(model, link.Value)),
            RemoveLinkActionId when link is not null =>
                new RemoveLinkCommand(body, link.Value.From, link.Value.To),
            RenameLoopActionId when loop is not null => new SetLoopNameCommand(body, loop, value),
            RemoveLoopActionId when loop is not null => new RemoveLoopCommand(body, loop),
            _ => null,
        };
    }

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
    private async ValueTask<ContextExecutionResult> AddVariableAsync(ContextTarget target, CausalLoopModel model, CancellationToken cancellationToken)
    {
        (string id, string label) = CausalLoopEdits.NextVariableName(model);

        if (CausalLoopSelection.PlacementPoint(target.ElementId) is { } point &&
            sessions.Find(target.WatchId, target.ResolvedFullPath) is CausalLoopSession session)
        {
            var refusal = await session.AddVariableAtAsync(id, label, point.X, point.Y, cancellationToken);
            return refusal.Length == 0 ? new ContextExecutionCompleted() : new ContextExecutionFailed(refusal);
        }

        return await DispatchAsync(target, new AddVariableCommand(target.ResolvedFullPath, id, label), cancellationToken);
    }

    /// <summary>
    /// Removing a variable takes the links and loops that named it, so the count is stated before
    /// anything runs rather than discovered afterwards: the definition's deletion confirmation.
    /// </summary>
    /// <remarks>
    /// A target the document does not hold has no confirmation to derive; it is asked about by the name
    /// its id carries, as it always was, and the removal it confirms then refuses.
    /// </remarks>
    private static ValueTask<ContextExecutionResult> Confirm(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, ContextTarget target)
    {
        var asked = CausalLoopDefinition.RemovalConfirmation(entry, target.ElementId);
        var request = asked is null
            ? new ContextConfirmationRequest("Remove variable", "mdi-delete-outline", $"Remove '{CausalLoopSelection.VariableOf(target.ElementId) ?? ""}'?", "Remove", Danger: true)
            : new ContextConfirmationRequest(asked.Title, "mdi-delete-outline", asked.Message, asked.ConfirmLabel, asked.Danger);

        return ValueTask.FromResult<ContextExecutionResult>(new ContextExecutionRequiresConfirmation(request));
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
            if (model.Loops.All(loop => loop.Identifier != candidate))
            {
                return candidate;
            }
        }

        return "R";
    }
}
