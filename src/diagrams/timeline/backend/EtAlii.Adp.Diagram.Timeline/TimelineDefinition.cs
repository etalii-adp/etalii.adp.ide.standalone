using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The timeline's bundled DISL definition (<c>definition/timeline.dis</c>, from etalii-adp/etalii.adp),
/// loaded once with the module's plugin functions (<see cref="TimelineTimes"/>) as a
/// <see cref="DislDiagramDefinition"/>, and what the providers derive from it: the palette, the
/// context menus, the property rows, the findings, the
/// placement additions and the removal's confirmation, mapped onto the host's types with the wire
/// ids of its <c>x-timeline</c> block; and the edits the DISL runtime runs from the definition's
/// operations and tools - adding after and below (with their <c>connect</c>), a relation dragged onto
/// empty canvas (its tool's <c>createTarget</c> or <c>createSource</c>), and giving and removing an end
/// (a <c>retype</c>) - each mapped onto the module's command, because every write is a splice of the
/// file's own lines that no DISL change says how to make.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the DISL model of the body the <see cref="TimelineModel"/> was read from</b>
/// (<see cref="TimelineBody.Disl"/>, built by <see cref="DislModelBuilder"/> from the binding's reading),
/// so a menu, a row and a finding read the same snapshot. <b>An element is found by the id written in
/// the document</b>, the binding's host attribute <c>storedId</c>: the first element with it, else the
/// first relation, as <see cref="TimelineEdits"/> finds it.
/// </para>
/// <para>
/// <b>The menus are derived editable</b>: the hand-written menus never asked whether a document could
/// be edited, and an unreadable document has no elements to offer anything on.
/// </para>
/// </remarks>
internal static class TimelineDefinition
{
    /// <summary>
    /// The definition, with its <c>x-timeline</c> wire ids. Arrange carries its sentence even while it is
    /// available, as the hand-written menu did.
    /// </summary>
    public static DislDiagramDefinition Definition { get; } = new(typeof(TimelineDefinition).Assembly, "timeline.dis", "x-timeline", TimelineTimes.Plugins, ["arrange"]);

    /// <summary>The host attribute holding the id an element or relation is written with, absent for none.</summary>
    private const string StoredId = "storedId";

    /// <summary>
    /// The reader's findings the tool reports: <c>std.unparseable</c> alone (timeline-disl-fbl Q2). The
    /// library's notes on a missing or repeated id and a dangling reference are reported by the
    /// definition's own rules under the tool's codes, and <c>fbl.duplicate-key</c>,
    /// <c>fbl.header-mismatch</c> and <c>std.unreadableEntry</c> are findings the tool does not give.
    /// </summary>
    private static readonly HashSet<string> ReportedReaderFindings = new(StringComparer.Ordinal) { FindingCodes.Unparseable };

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
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>What the runtime had to say about the definition as it loaded it.</summary>
    public static IReadOnlyList<LoadDiagnostic> Diagnostics => Definition.Diagnostics;

    /// <summary>What <c>env</c> reads: editable, in the one viewpoint a timeline has.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="model"/>: an element, a relation, a placement, a relation gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(TimelineModel model, string elementId)
    {
        var diagram = DiagramOf(model);
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParseRowPlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, ElementOf(diagram, from), ElementOf(diagram, to), "Connection")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="model"/>: an element's or a relation's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(TimelineModel model, string elementId)
    {
        // Every row is a line: the definition's widgets are text and number, there is no date-aware
        // editor (property-grid Requirement 9), and a row is typed as text too.
        return ElementOf(DiagramOf(model), elementId) is { } element ? Definition.Rows(element, Env) : [];
    }

    /// <summary>
    /// The findings of <paramref name="body"/>, grouped as the tool reports them: the ids, then the
    /// times, then the relations' ends; within a group by declaration, elements before relations, and
    /// within a declaration in the order the definition states its rules. A body the library cannot
    /// read has the one finding <c>std.unparseable</c>, with the library's reason.
    /// </summary>
    public static IReadOnlyList<DiagramProblem> Problems(TimelineBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var built = body.Disl;
        var declarations = Declarations(built.Diagram);
        var reader = body.Model.Findings
            .Where(finding => ReportedReaderFindings.Contains(finding.Code))
            .Select(finding => DislReaderFinding.NotParsed(finding.Message, finding.Location?.Line))
            .ToList();
        var findings = ConstraintEvaluator.Evaluate(Specification, built.Diagram, new DislConstraintOptions(Env, reader, WrittenIdOf));
        return
        [
            .. findings
                .Select((finding, arising) => (Finding: finding, Arising: arising))
                .OrderBy(entry => FindingGroups.GetValueOrDefault(entry.Finding.Code, int.MaxValue))
                .ThenBy(entry => entry.Finding.Line is { } line ? declarations.GetValueOrDefault(line, int.MaxValue) : int.MaxValue)
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
            WorkingOf(model),
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
        var diagram = WorkingOf(model);
        if (ElementOf(diagram, elementId) is not { Kind: "node" } self) return (null, "That is no longer in this timeline.");

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
        var diagram = WorkingOf(model);
        if (ElementOf(diagram, elementId) is not { Kind: "node" } existing) return (null, "That is no longer in this timeline.");

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
        var diagram = WorkingOf(model);
        if (ElementOf(diagram, elementId) is not { Kind: "node" } self || !OperationInterpreter.AppliesTo(Specification, operation, self)) return (null, "");

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
            || (newElementIsSource ? relation.SourceId : relation.TargetId, newElementIsSource ? relation.TargetId : relation.SourceId) != (created.Id, existingId))
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
        ElementOf(DiagramOf(model), elementId) is { Kind: "node" } element
            ? DeletionPolicy.Confirmation(Specification, element, env: Env)
            : null;

    /// <summary>The id <paramref name="element"/> is written with, the binding's <c>storedId</c>; empty for none.</summary>
    private static string WrittenIdOf(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.HostAttributes.TryGetValue(StoredId, out var id) ? id as string ?? "" : "";
    }

    /// <summary>The element, else the relation, written with <paramref name="id"/>, as <see cref="TimelineEdits"/> finds it; null for none.</summary>
    private static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        if (id is null) return null;
        return diagram.Nodes.FirstOrDefault(node => WrittenIdOf(node) == id)
            ?? diagram.Relations.FirstOrDefault(relation => WrittenIdOf(relation) == id);
    }

    /// <summary>The DISL diagram of the body <paramref name="model"/> was read from; read it, never change it. Empty for a model no body was read into.</summary>
    private static DislDiagram DiagramOf(TimelineModel model) =>
        TimelineReading.BodyOf(model)?.Disl.Diagram ?? Unread(model);

    /// <summary>A DISL diagram of the body <paramref name="model"/> was read from of its own, for an operation to change as it runs.</summary>
    private static DislDiagram WorkingOf(TimelineModel model) =>
        TimelineReading.BodyOf(model)?.Working() ?? Unread(model);

    /// <summary>The diagram of a model no body was read into: only the empty model, the one of a body that cannot be read, has none.</summary>
    private static DislDiagram Unread(TimelineModel model) =>
        model.Elements.Count == 0 && model.Connections.Count == 0
            ? new DislDiagram(Specification)
            : throw new InvalidOperationException("A timeline model is derived from only when TimelineReading read it from a body.");

    /// <summary>Each element's and relation's place among the declarations, elements first, by the line it starts on.</summary>
    private static Dictionary<int, int> Declarations(DislDiagram diagram)
    {
        var declarations = new Dictionary<int, int>();
        foreach (var element in diagram.Nodes.OrderBy(node => node.Line).Concat(diagram.Relations.OrderBy(relation => relation.Line)))
        {
            if (element.Line is { } line) declarations.TryAdd(line, declarations.Count);
        }

        return declarations;
    }

    private static DiagramProblem Problem(DislFinding finding)
    {
        // The ids and the file are reported at the line, because an element without an id, or with one
        // another declaration holds, cannot be named by it; everything else names its element.
        DiagramProblemLocation location = finding.Code is TimelineRules.MissingId or TimelineRules.DuplicateId or TimelineValidator.UnparseableRuleId
            ? new DiagramProblemLineLocation((uint)(finding.Line ?? 1))
            : new DiagramProblemElementLocation(finding.ElementIds is [var id, ..] ? id : "");
        return new DiagramProblem(DislDiagramDefinition.SeverityOf(finding), finding.Message, finding.Code, location);
    }

    private static string? Text(IReadOnlyDictionary<string, object?> attributes, string name) =>
        attributes.TryGetValue(name, out var value) ? value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
}
