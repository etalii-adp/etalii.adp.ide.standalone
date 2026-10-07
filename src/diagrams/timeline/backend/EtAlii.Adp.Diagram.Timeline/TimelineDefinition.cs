using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The timeline's bundled DISL definition (<c>definition/timeline.dis</c>, from etalii-adp/etalii.adp),
/// loaded once with the module's plugin functions (<see cref="TimelineTimes"/>), and what the
/// providers derive from it: the palette, the context menus, the property rows, the findings, the
/// placement additions and the removal's confirmation, mapped onto the host's types with the wire
/// ids of its <c>x-timeline</c> block; and the edits the DISL runtime runs from the definition's
/// operations and tools - adding after and below (with their <c>connect</c>), a relation dragged onto
/// empty canvas (its tool's <c>createTarget</c> or <c>createSource</c>), and giving and removing an end
/// (a <c>retype</c>) - each mapped onto the module's command, because every write is a splice of the
/// file's own lines that no DISL change says how to make.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the parsed document's</b> (<see cref="TimelineDisl"/>), so a menu, a row and a
/// finding read the same snapshot. <b>An element is found by the id written in the document</b>, the
/// first element with it, else the first relation, as <see cref="TimelineEdits"/> finds it.
/// </para>
/// <para>
/// <b>The menus are derived editable</b>: the hand-written menus never asked whether a document could
/// be edited, and an unreadable document has no elements to offer anything on.
/// </para>
/// </remarks>
internal static class TimelineDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(TimelineDefinition).Assembly, "timeline.dis", TimelineTimes.Plugins()));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-timeline"));

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, Ids)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
    ]);

    private static readonly Lazy<string> LoadedArrangeReason = new(ArrangeReasonOf);

    /// <summary>The order the findings are reported in: the ids, then the times, then the relations' ends.</summary>
    private static readonly Dictionary<string, int> FindingGroups = new(StringComparer.Ordinal)
    {
        [TimelineRules.MissingId] = 0,
        [TimelineRules.DuplicateId] = 0,
        [TimelineRules.UnreadableTime] = 1,
        [TimelineRules.EndBeforeBegin] = 1,
        [TimelineRules.MixedPrecision] = 1,
        [TimelineRules.DanglingConnection] = 2,
    };

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>What the runtime had to say about the definition as it loaded it.</summary>
    public static IReadOnlyList<DislDiagnostic> Diagnostics => Loaded.Value.Diagnostics;

    /// <summary>The wire ids of <c>x-timeline</c>.</summary>
    public static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>What <c>env</c> reads: editable, in the one viewpoint a timeline has.</summary>
    public static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="model"/>: an element, a relation, a placement, a relation gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(TimelineModel model, string elementId)
    {
        var diagram = TimelineDisl.Of(model).Diagram;
        DislMenuTarget? target = TimelineDisl.ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : TimelineNewPlacement.TryParse(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : TimelineRelationGesture.TryParse(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, TimelineDisl.ElementOf(diagram, from), TimelineDisl.ElementOf(diagram, to), "Connection")
                    : null;
        if (target is null) return [];

        return
        [
            .. ContextMenuDerivation.Derive(Specification, target, Env, Ids)
                .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
        ];
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="model"/>: an element's or a relation's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(TimelineModel model, string elementId)
    {
        if (TimelineDisl.ElementOf(TimelineDisl.Of(model).Diagram, elementId) is not { } element) return [];

        // Every row is a line: there is no date-aware editor (property-grid Requirement 9), and a row is typed as text too.
        return [.. FormDerivation.Derive(Specification, element, Env, Ids).Select(row => new ContextPropertyDefinition(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group))];
    }

    /// <summary>
    /// The findings of <paramref name="model"/>, grouped as the tool reports them: the ids, then the
    /// times, then the relations' ends; within a group by declaration, elements before relations, and
    /// within a declaration in the order the definition states its rules.
    /// </summary>
    public static IReadOnlyList<DiagramProblem> Problems(TimelineModel model)
    {
        var built = TimelineDisl.Of(model);
        var findings = ConstraintEvaluator.Evaluate(Specification, built.Diagram, new DislConstraintOptions(Env, WrittenId: TimelineDisl.WrittenIdOf));
        return
        [
            .. findings
                .Select((finding, arising) => (Finding: finding, Arising: arising))
                .OrderBy(entry => FindingGroups.GetValueOrDefault(entry.Finding.Code, int.MaxValue))
                .ThenBy(entry => entry.Finding.Line is { } line ? built.Declarations.GetValueOrDefault(line, int.MaxValue) : int.MaxValue)
                .ThenBy(entry => entry.Arising)
                .Select(entry => Problem(entry.Finding)),
        ];
    }

    /// <summary>The one finding of a document that is not YAML: the reader's <c>std.unparseable</c>, at the parser's line.</summary>
    public static IReadOnlyList<DiagramProblem> Unparseable(string reason, int line)
    {
        var findings = ConstraintEvaluator.Evaluate(
            Specification,
            new DislDiagram(Specification),
            new DislConstraintOptions(Env, [DislReaderFinding.NotParsed(reason, line)]));
        return [.. findings.Select(Problem)];
    }

    /// <summary>
    /// The element a placement addition creates: <paramref name="operation"/> (<c>addElementHere</c> or
    /// <c>addMomentHere</c>) run at <paramref name="begin"/> and <paramref name="row"/>, under <paramref name="id"/>.
    /// </summary>
    public static AddTimelineElementCommand Addition(TimelineModel model, string body, string operation, string id, string begin, int row)
    {
        var transaction = OperationInterpreter.Run(
            Specification,
            operation,
            TimelineDisl.Build(model).Diagram,
            null,
            DislIds.Fixed(id),
            new DislInvocation(new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = begin, ["y"] = (long)row }),
            Env);
        if (!transaction.WasApplied || transaction.Changes is not [DislChange.Create created])
        {
            throw new InvalidOperationException($"The definition's {operation} does not create one element: {transaction.Refusal}");
        }

        return new AddTimelineElementCommand(
            body,
            created.Id,
            Text(created.Attributes, "label") ?? "",
            Text(created.Attributes, "begin") ?? "",
            created.Type == "Period" ? Text(created.Attributes, "end") ?? "" : null,
            created.Attributes.TryGetValue("row", out var written) && written is long value ? (int)value : 0);
    }

    /// <summary>
    /// The element <paramref name="operation"/> (<c>addAfter</c> or <c>addBelow</c>) grows from
    /// <paramref name="elementId"/>, and the relation from it, as the definition computes them: one command,
    /// under two new ids; or, when the operation refuses or makes anything else, why not.
    /// </summary>
    public static (AddConnectedTimelineElementCommand? Command, string Refusal) Grown(TimelineModel model, string body, string operation, string elementId)
    {
        var diagram = TimelineDisl.Build(model).Diagram;
        if (TimelineDisl.ElementOf(diagram, elementId) is not { Kind: "node" } self) return (null, "That is no longer in this timeline.");

        var transaction = OperationInterpreter.Run(Specification, operation, diagram, self, NewIds(), null, Env);
        return Related(transaction, body, elementId, operation, newElementIsSource: false, self.Id);
    }

    /// <summary>
    /// A relation gesture released on empty canvas at <paramref name="begin"/> and <paramref name="row"/>:
    /// the element the definition's relation tool creates there, as the gesture's <paramref name="newEnd"/>
    /// (<c>source</c> from a begin anchor, <c>target</c> from an end anchor), and the relation between it and
    /// <paramref name="elementId"/>, as one command; or why not.
    /// </summary>
    public static (AddConnectedTimelineElementCommand? Command, string Refusal) RelatedHere(TimelineModel model, string body, string elementId, string newEnd, string begin, int row)
    {
        var diagram = TimelineDisl.Build(model).Diagram;
        if (TimelineDisl.ElementOf(diagram, elementId) is not { Kind: "node" } existing) return (null, "That is no longer in this timeline.");

        var transaction = OperationInterpreter.ConnectToNew(
            Specification,
            "Connection",
            diagram,
            existing,
            newEnd,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = begin, ["y"] = (long)row },
            NewIds(),
            Env);
        return Related(transaction, body, elementId, "relation tool", newElementIsSource: newEnd == "source", existing.Id);
    }

    /// <summary>
    /// What <paramref name="operation"/> (<c>giveEnd</c> or <c>removeEnd</c>) does to <paramref name="elementId"/>,
    /// with <paramref name="end"/> as its <c>end</c> parameter: the end it writes, or none, as one command; or, when
    /// it refuses, why not. Null and no refusal when the operation is not for that element at all.
    /// </summary>
    public static (SetTimelineEndCommand? Command, string Refusal) EndChange(TimelineModel model, string body, string operation, string elementId, string? end)
    {
        var diagram = TimelineDisl.Build(model).Diagram;
        if (TimelineDisl.ElementOf(diagram, elementId) is not { Kind: "node" } self || !OperationInterpreter.AppliesTo(Specification, operation, self)) return (null, "");

        var parameters = end is null ? null : new Dictionary<string, object?>(StringComparer.Ordinal) { ["end"] = end };
        var transaction = OperationInterpreter.Run(Specification, operation, diagram, self, DislIds.Fixed(), new DislInvocation(Parameters: parameters), Env);
        if (!transaction.WasApplied) return (null, transaction.Refusal!);

        var written = transaction.Changes.OfType<DislChange.Set>().Where(set => set.ElementId == self.Id && set.Attributes.ContainsKey("end")).ToList();
        if (written.Count != 1 || transaction.Changes.Any(change => change is not (DislChange.Set or DislChange.Retype)))
        {
            return (null, $"The definition's {operation} does not change one element's end.");
        }

        // An unset end is null - no end line - where the other attributes' text would read it as empty.
        return (new SetTimelineEndCommand(body, elementId, written[0].Attributes["end"] is null ? null : Text(written[0].Attributes, "end")), "");
    }

    /// <summary>Two ids for an element and its relation, minted in that order as the hand-written commands did.</summary>
    private static IIdSource NewIds() => DislIds.Fixed(ShortGuid.NewShortGuid().ToString(), ShortGuid.NewShortGuid().ToString());

    /// <summary>A transaction that creates one element and relates it to <paramref name="existingId"/>, as the command that writes both.</summary>
    private static (AddConnectedTimelineElementCommand? Command, string Refusal) Related(DislTransaction transaction, string body, string elementId, string what, bool newElementIsSource, string existingId)
    {
        if (!transaction.WasApplied) return (null, transaction.Refusal!);
        if (transaction.Changes is not [DislChange.Create { Type: "Period" or "Moment" } created, DislChange.Connect { Type: "Connection" } relation]
            || (newElementIsSource ? (relation.SourceId, relation.TargetId) : (relation.TargetId, relation.SourceId)) != (created.Id, existingId))
        {
            return (null, $"The definition's {what} does not add one element related to this one.");
        }

        return (new AddConnectedTimelineElementCommand(
            body,
            elementId,
            created.Id,
            relation.Id,
            Text(created.Attributes, "begin") ?? "",
            created.Type == "Period" ? Text(created.Attributes, "end") ?? "" : null,
            created.Attributes.TryGetValue("row", out var row) && row is long value ? checked((int)value) : 0,
            newElementIsSource,
            Text(created.Attributes, "label") ?? ""), "");
    }

    /// <summary>What removing <paramref name="elementId"/> asks first; null when it asks nothing.</summary>
    public static DislConfirmation? RemoveConfirmation(TimelineModel model, string elementId) =>
        TimelineDisl.ElementOf(TimelineDisl.Of(model).Diagram, elementId) is { Kind: "node" } element
            ? DeletionPolicy.Confirmation(Specification, element, env: Env)
            : null;

    private static DiagramProblem Problem(DislFinding finding)
    {
        var severity = finding.Severity == "error" ? DiagramProblemSeverity.Error : DiagramProblemSeverity.Warning;

        // The ids and the file are reported at the line, because an element without an id, or with one
        // another declaration holds, cannot be named by it; everything else names its element.
        DiagramProblemLocation location = finding.Code is TimelineRules.MissingId or TimelineRules.DuplicateId or TimelineValidator.UnparseableRuleId
            ? new DiagramProblemLineLocation((uint)(finding.Line ?? 1))
            : new DiagramProblemElementLocation(finding.ElementIds is [var id, ..] ? id : "");
        return new DiagramProblem(severity, finding.Message, finding.Code, location);
    }

    private static ContextActionDefinition Action(DerivedMenuEntry entry) => new(
        entry.Id,
        entry.Label,
        entry.Icon,
        entry.Shortcut is { } key ? new ContextShortcutDefinition(key.Key, key.Ctrl, key.Shift, key.Alt, key.Meta) : null,
        entry.Available,
        // Arrange carries its sentence even while it is available, as the hand-written menu did; the
        // client shows a reason only for an unavailable action.
        entry.Available && entry.Operation == "arrange" ? LoadedArrangeReason.Value : entry.UnavailableReason);

    private static string? Text(IReadOnlyDictionary<string, object?> attributes, string name) =>
        attributes.TryGetValue(name, out var value) ? value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) : null;

    /// <summary>The sentence of Arrange's one <c>unavailable</c> reason, as the definition writes it.</summary>
    private static string ArrangeReasonOf() =>
        Specification.Root.GetProperty("behavior").GetProperty("operations").GetProperty("arrange").GetProperty("unavailable")
            .EnumerateArray().Select(reason => reason.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String ? message.GetString() : null)
            .FirstOrDefault(message => message is not null) ?? "";
}
