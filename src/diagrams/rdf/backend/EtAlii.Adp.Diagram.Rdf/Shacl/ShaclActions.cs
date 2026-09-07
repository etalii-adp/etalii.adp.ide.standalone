using EtAlii.Adp.Common;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// The shapes reading's gestures, offered and executed through the family provider's one
/// registration (shacl-diagram Requirement 6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Scoped by origin, deliberately.</b> Every reading of this family shares one selection
/// vocabulary, so an element id cannot say which reading is looking at it: two registrations
/// over one <c>.ttl</c> produce byte-identical targets. These cases therefore answer only for a
/// target whose origin is <c>w3c/shacl</c> - the family's own cases answer for any origin in the
/// family, and where both answer this reading's group is listed first. Delegating inside the one
/// provider rather than registering beside it is what makes that order explicit: discovery
/// concatenates groups, but execution and shortcut resolution take the first match.
/// </para>
/// <para>
/// Availability is decided by <see cref="ShaclEditGate"/>, which is also the provider half of
/// Requirement 3.3's double refusal: it answers without consulting the writer, so a blank-rooted
/// selection is marked unavailable before anything is executed, and the writer refuses the same
/// edit independently if it is ever called directly.
/// </para>
/// </remarks>
public static class ShaclActions
{
    /// <summary>Declare a class target on a shape (Requirement 5.3).</summary>
    public const string AddTargetClassActionId = "shacl.add-target-class";

    /// <summary>Declare a node target on a shape.</summary>
    public const string AddTargetNodeActionId = "shacl.add-target-node";

    /// <summary>Add a property row to a shape (Requirement 5.4).</summary>
    public const string AddPropertyRowActionId = "shacl.add-property-row";

    /// <summary>Switch a shape off, or back on (Requirement 5.5).</summary>
    public const string DeactivateActionId = "shacl.deactivate";

    /// <inheritdoc cref="DeactivateActionId" />
    public const string ReactivateActionId = "shacl.reactivate";

    /// <summary>Remove a shape and the blank subtrees only it reaches (Requirement 5.6).</summary>
    public const string RemoveShapeActionId = "shacl.remove-shape";

    /// <summary>Withdraw one target declaration; the chip's address rides the id after this prefix.</summary>
    public const string RemoveTargetActionIdPrefix = "shacl.remove-target|";

    /// <summary>Create a node shape at a placement (Requirement 5.2).</summary>
    public const string AddNodeShapeActionId = "shacl.add-node-shape";

    /// <summary>The shacl entries for a discovery pass, or empty where the selection is not this reading's.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Discover(RdfDocumentEntry entry, ContextTarget target)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);

        if (!AnswersFor(target))
        {
            return [];
        }

        if (RdfNewPlacement.TryParse(target.ElementId, out _, out _))
        {
            return
            [
                new ContextActionGroupDefinition(
                    [new ContextActionDefinition(AddNodeShapeActionId, "Add node shape here…", "mdi-check-decagram-outline")]),
            ];
        }

        var decision = ShaclEditGate.For(entry.Model, target.ElementId, RdfSelection.IsTruncated(entry));
        if (!decision.Applies)
        {
            return [];
        }

        var available = decision.Available;
        var reason = decision.Reason;
        var card = ShaclProjection.Project(entry.Model).Cards
            .FirstOrDefault(candidate => candidate.Id == target.ElementId);

        var actions = new List<ContextActionDefinition>
        {
            new(AddTargetClassActionId, "Add target class…", "mdi-crosshairs", null, available, reason),
            new(AddTargetNodeActionId, "Add target node…", "mdi-crosshairs-gps", null, available, reason),
            new(AddPropertyRowActionId, "Add property row…", "mdi-table-row-plus-after", null, available, reason),
        };

        if (card is not null)
        {
            actions.Add(card.Deactivated
                ? new ContextActionDefinition(ReactivateActionId, "Reactivate shape", "mdi-play-circle-outline", null, available, reason)
                : new ContextActionDefinition(DeactivateActionId, "Deactivate shape", "mdi-pause-circle-outline", null, available, reason));

            // One entry per declared target, addressed the way the chip carries it - so a chip
            // is removable without ever being a selectable element (Requirement 1.3's contract).
            foreach (var chip in card.Targets.Where(chip => chip.PredicateIri.Length > 0 && chip.TermIri.Length > 0))
            {
                actions.Add(new ContextActionDefinition(
                    RemoveTargetActionIdPrefix + chip.PredicateIri + "|" + chip.TermIri,
                    $"Remove target: {KindWord(chip.Kind)} {chip.TermDisplay}",
                    "mdi-crosshairs-off",
                    null,
                    available,
                    reason));
            }

            var count = decision.ShapeIri.Length > 0
                ? ShaclWriter.CountShapeRemoval(entry.Model, decision.ShapeIri)
                : 0;
            actions.Add(new ContextActionDefinition(
                RemoveShapeActionId,
                count > 1 ? $"Remove shape (with {count} statements)" : "Remove shape",
                "mdi-delete-outline",
                new ContextShortcutDefinition("Delete"),
                available,
                reason));
        }

        return [new ContextActionGroupDefinition(actions)];
    }

    /// <summary>Executes a shacl action, or null when <paramref name="actionId"/> is not this reading's.</summary>
    public static async ValueTask<ContextExecutionResult?> ExecuteAsync(
        IHistoryStackStore historyStacks,
        RdfDocumentEntry entry,
        ContextTarget target,
        string actionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);

        if (!IsShaclAction(actionId) || !AnswersFor(target))
        {
            return null;
        }

        // The writer refuses this independently; the gate refuses it here without asking the
        // writer, so neither layer relies on the other (Requirement 3.3).
        var decision = ShaclEditGate.For(entry.Model, target.ElementId, RdfSelection.IsTruncated(entry));
        if (decision.Applies && !decision.Available)
        {
            return new ContextExecutionFailed(decision.Reason);
        }

        switch (actionId)
        {
            case AddTargetClassActionId:
                return Ask("Add target class", "mdi-crosshairs", "Class IRI or prefixed name");

            case AddTargetNodeActionId:
                return Ask("Add target node", "mdi-crosshairs-gps", "Node IRI or prefixed name");

            case AddPropertyRowActionId:
                return Ask("Add property row", "mdi-table-row-plus-after", "Path IRI or prefixed name");

            case AddNodeShapeActionId:
                return Ask("Add node shape", "mdi-check-decagram-outline", "Shape IRI or prefixed name");

            case DeactivateActionId or ReactivateActionId:
            {
                if (decision.ShapeIri.Length == 0)
                {
                    return new ContextExecutionFailed(ShaclRefusals.BlankRooted);
                }

                return await DispatchAsync(
                    historyStacks,
                    target,
                    new SetShaclDeactivatedCommand(target.ResolvedFullPath, decision.ShapeIri, actionId == DeactivateActionId),
                    cancellationToken);
            }

            case RemoveShapeActionId:
            {
                if (decision.ShapeIri.Length == 0)
                {
                    return new ContextExecutionFailed(ShaclRefusals.BlankRooted);
                }

                return await DispatchAsync(
                    historyStacks,
                    target,
                    new RemoveShaclShapeCommand(target.ResolvedFullPath, decision.ShapeIri),
                    cancellationToken);
            }

            default:
            {
                if (!actionId.StartsWith(RemoveTargetActionIdPrefix, StringComparison.Ordinal))
                {
                    return null;
                }

                var parts = actionId[RemoveTargetActionIdPrefix.Length..].Split('|');
                if (parts.Length != 2 || decision.ShapeIri.Length == 0)
                {
                    return new ContextExecutionFailed(ShaclRefusals.NoSuchTarget);
                }

                return await DispatchAsync(
                    historyStacks,
                    target,
                    new RemoveShaclTargetCommand(target.ResolvedFullPath, decision.ShapeIri, parts[0], parts[1]),
                    cancellationToken);
            }
        }
    }

    /// <summary>Validates a dialog value, or null when the action is not this reading's.</summary>
    public static ContextValidationResult? Validate(RdfDocumentEntry entry, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (actionId is not (AddTargetClassActionId or AddTargetNodeActionId or AddPropertyRowActionId or AddNodeShapeActionId))
        {
            return null;
        }

        var (iri, error) = RdfTermInput.Resolve(entry.Model, value);
        return iri is null ? ContextValidationResult.Rejected(error) : ContextValidationResult.Accepted;
    }

    /// <summary>The command a dialog value commits to, or null when the action is not this reading's.</summary>
    public static ICommand? CommandFor(RdfDocumentEntry entry, ContextTarget target, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(target);

        if (Validate(entry, actionId, value) is not { Valid: true })
        {
            return null;
        }

        var (iri, _) = RdfTermInput.Resolve(entry.Model, value);
        if (iri is null)
        {
            return null;
        }

        if (actionId == AddNodeShapeActionId)
        {
            return new CreateShaclNodeShapeCommand(target.ResolvedFullPath, iri);
        }

        var shapeIri = ShaclEditGate.For(entry.Model, target.ElementId, truncated: false).ShapeIri;
        if (shapeIri.Length == 0)
        {
            return null;
        }

        return actionId switch
        {
            AddTargetClassActionId => new AddShaclTargetCommand(target.ResolvedFullPath, shapeIri, ShaclVocabulary.TargetClass, iri),
            AddTargetNodeActionId => new AddShaclTargetCommand(target.ResolvedFullPath, shapeIri, ShaclVocabulary.TargetNode, iri),
            AddPropertyRowActionId => new AddShaclPropertyRowCommand(target.ResolvedFullPath, shapeIri, iri),
            _ => null,
        };
    }

    /// <summary>Whether this reading owns the target: its origin, and nothing else's.</summary>
    private static bool AnswersFor(ContextTarget target) =>
        target.Origin is null || target.Origin == ServiceCollectionAddShaclExtension.ShaclOrigin;

    private static bool IsShaclAction(string actionId) =>
        actionId.StartsWith("shacl.", StringComparison.Ordinal);

    private static ContextExecutionResult Ask(string title, string icon, string placeholder) =>
        new ContextExecutionRequiresInput(new ContextInputRequest(title, icon, placeholder, "", "Add"));

    private static async ValueTask<ContextExecutionResult> DispatchAsync(
        IHistoryStackStore historyStacks,
        ContextTarget target,
        ICommand command,
        CancellationToken cancellationToken)
    {
        var result = await historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? new ContextExecutionCompleted() : new ContextExecutionFailed(result.Error);
    }

    private static string KindWord(ShaclTargetKind kind) => kind switch
    {
        ShaclTargetKind.Class => "class",
        ShaclTargetKind.Node => "node",
        ShaclTargetKind.SubjectsOf => "subjects of",
        ShaclTargetKind.ObjectsOf => "objects of",
        _ => "implicit class",
    };
}
