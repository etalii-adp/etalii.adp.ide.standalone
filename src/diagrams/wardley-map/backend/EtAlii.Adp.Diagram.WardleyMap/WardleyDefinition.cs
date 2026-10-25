using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// The Wardley map's bundled DISL definition (<c>definition/wardley-map.dis</c>, from etalii-adp/etalii.adp), loaded once
/// as a <see cref="DislDiagramDefinition"/> with <see cref="WardleyFunctions"/>, and what the providers derive from it:
/// the palette, the context menus, the property rows, what removing an element asks first, and what running, asking
/// for, refusing and committing an entry or a row does, mapped onto the host's types with the wire ids of its
/// <c>x-wardley</c> block and onto today's commands by <see cref="WardleyTransactionCommands"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is <see cref="WardleyDisl"/>'s</b>, built from the parser's reading and the identities the store keeps
/// beside the map, so a menu and a row read the same snapshot. An element is found by its identity, which is its model id.
/// An operation runs on a model of its own, built from the same reading, as the DISL runtime changes the one it is given.
/// </para>
/// <para>
/// <b>A file that cannot be written is derived read-only</b>: <c>env.readOnly</c> is true, so the menus are empty and
/// every row gives the definition's <c>readOnlyFile</c> reason, "This map's file is read-only.". Whether it can be written
/// stays the host's fact (<see cref="WardleyEditability"/>).
/// </para>
/// <para>
/// <b>A map that could not be read offers nothing</b>: its emptiness is not the document, so nothing may be added to it.
/// </para>
/// <para>
/// <b>An entry is found by its wire id among the entries its target's menu offers</b>, and, for an operation the menu
/// does not show now, by the id <c>x-wardley</c> gives the operation, so that it is refused as it would refuse: Stop
/// evolving on an element that is not evolving says so.
/// </para>
/// <para>
/// <b>What DISL cannot say is the host's</b> (wardley-map.md, gap 11): a picker's confirming button says Link or Flow, by
/// the relation it draws; a choice of one link asks nothing; and a dialog whose one field edits the element's label,
/// opening on it, opens over that label on the canvas.
/// </para>
/// </remarks>
internal static class WardleyDefinition
{
    /// <summary>The definition, with its <c>x-wardley</c> wire ids.</summary>
    internal static DislDiagramDefinition Definition { get; } = new(
        typeof(WardleyDefinition).Assembly,
        "wardley-map.dis",
        "x-wardley",
        WardleyFunctions.Plugins);

    /// <summary>The refusal of anything done to an element the map no longer has.</summary>
    private const string Gone = "That element is no longer on this map.";

    /// <summary>The confirming button of a picker, by the relation it draws (wardley-map.md, gap 11).</summary>
    private static readonly Dictionary<string, string> PickerConfirmLabels = new(StringComparer.Ordinal)
    {
        ["Dependency"] = "Link",
        ["Flow"] = "Flow",
    };

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => Definition.Toolbox;

    /// <summary>
    /// Where a type's findings of one code come among the others' (the tool reports by rule and then by collection): the
    /// positioned elements, the pipelines with their components, the evolutions, the notes, the accelerators, the
    /// attitudes and the annotations. A pipeline and its components share a place, so by line a legacy pipeline's extent
    /// and a component's maturity come pipeline by pipeline. A type it does not name comes last.
    /// </summary>
    private static readonly string[][] FindingTypes = [["Positioned"], ["Pipeline", "PipelineComponent"], ["Evolution"], ["Note"], ["Accelerator"], ["Attitude"], ["Annotation"]];

    /// <summary>The one code reported where a map's elements cannot be located by line alone: at the first holder's id.</summary>
    private const string DuplicateName = "wardley.duplicate-name";

    /// <summary>What <c>env</c> reads for a map whose file can be written: editable, in no viewpoint.</summary>
    internal static DislEnv Env { get; } = new();

    /// <summary>What <c>env</c> reads for a map whose file cannot be written: read-only, in no viewpoint.</summary>
    private static DislEnv ReadOnlyEnv { get; } = new(ReadOnly: true);

    /// <summary>
    /// The context menu of <paramref name="elementId"/> on the map <paramref name="entry"/> holds, with the
    /// <paramref name="identities"/> the store keeps beside it: an element's, or the map's own for the empty id; none for
    /// anything else, and none on a map that could not be read.
    /// </summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<LineDocument, WardleyMap> entry, IReadOnlyList<WardleyIdentityEntry> identities, string elementId, bool editable)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable) return [];

        var diagram = WardleyDisl.Of(entry.Document, entry.Model, identities);
        return TargetOf(diagram, elementId) is { } target ? Definition.Menus(target, EnvOf(editable)) : [];
    }

    /// <summary>The property rows of <paramref name="elementId"/> on the map <paramref name="entry"/> holds: its form's; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, WardleyMap> entry, IReadOnlyList<WardleyIdentityEntry> identities, string elementId, bool editable)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return WardleyDisl.ElementOf(WardleyDisl.Of(entry.Document, entry.Model, identities), elementId) is { } element
            ? Definition.Rows(element, EnvOf(editable))
            : [];
    }

    /// <summary>
    /// What running <paramref name="actionId"/> on <paramref name="elementId"/> (the map for the empty id) does: asks
    /// what the entry's params form asks, or its picker, first; or asks to confirm a removal; or the command it writes;
    /// or why not.
    /// </summary>
    public static WardleyStep Execute(DocumentEntry<LineDocument, WardleyMap> entry, IReadOnlyList<WardleyIdentityEntry> identities, string bodyPath, string elementId, string actionId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = WardleyDisl.Of(entry.Document, entry.Model, identities);
        if (elementId.Length > 0 && WardleyDisl.ElementOf(diagram, elementId) is null) return WardleyStep.Refused(Gone);
        var self = WardleyDisl.ElementOf(diagram, elementId);
        if (EntryOf(diagram, self, actionId) is not { } found) return WardleyStep.Refused(DoesNotApply(actionId));

        switch (found)
        {
            case { Kind: "delete" } when self is not null:
                return Removal(bodyPath, diagram, self) is { } removal
                    ? WardleyStep.Removing(removal, ConfirmationOf(self))
                    : WardleyStep.Refused(WardleyTransactionCommands.Unwritable("deletion"));

            case { Kind: "connect", Picker: { } picker } when self is not null:
                return WardleyStep.Asking(new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                    picker.Title,
                    found.Icon,
                    PickerConfirmLabels.GetValueOrDefault(found.Via ?? "", found.Label),
                    [.. picker.Candidates.Select(candidate => new ContextOptionNode(candidate.ElementId, candidate.Label, Selectable: true))],
                    picker.EmptyText ?? "")));

            case { Operation: { } operation }:
                {
                    if (Unanswered(operation, found.Arguments) is { } form)
                    {
                        var asked = FormCommit.Request(Specification, form, self, Env, diagram)
                            ?? throw new InvalidOperationException($"The definition has no form '{form}'.");
                        if (asked.Fields is not [var field]) throw new InvalidOperationException($"The definition's form '{form}' asks for other than one value.");
                        if (field.Options is not { } options) return WardleyStep.Asking(new ContextExecutionRequiresInput(Input(asked, field, self)));

                        // A choice of one is no choice: it runs on that one (wardley-map.md, gap 11), and a choice of none
                        // runs to be refused as the operation refuses it.
                        if (options.Count > 1)
                        {
                            return WardleyStep.Asking(new ContextExecutionRequiresChoice(new ContextChoiceRequest(
                                asked.Label,
                                asked.Icon ?? "",
                                asked.SubmitLabel ?? "",
                                [.. options.Select(option => new ContextOptionNode(WardleyDisl.ChoiceIdOf(option.Value), option.Label, Selectable: true))],
                                Sentence(operation) ?? "")));
                        }

                        return Ran(bodyPath, entry, identities, diagram, operation, self, Answered(found.Arguments, field.Attribute, options is [var only] ? only.Value : null));
                    }

                    return Ran(bodyPath, entry, identities, diagram, operation, self, found.Arguments);
                }

            default:
                return WardleyStep.Refused(DoesNotApply(actionId));
        }
    }

    /// <summary>Why <paramref name="value"/> is refused for what <paramref name="actionId"/> asked on <paramref name="elementId"/>: its params form's validation; null when it is accepted.</summary>
    public static string? Validate(DocumentEntry<LineDocument, WardleyMap> entry, IReadOnlyList<WardleyIdentityEntry> identities, string elementId, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = WardleyDisl.Of(entry.Document, entry.Model, identities);
        var self = WardleyDisl.ElementOf(diagram, elementId);
        if (elementId.Length > 0 && self is null) return null;
        if (EntryOf(diagram, self, actionId) is not { Operation: { } operation } found || Unanswered(operation, found.Arguments) is not { } form) return null;

        var field = FormCommit.Request(Specification, form, self, Env, diagram)?.Fields.FirstOrDefault();
        return field is null ? null : FormCommit.Refusal(Specification, form, field.Attribute, value, self, Env, diagram);
    }

    /// <summary>
    /// What committing <paramref name="value"/>, asked for by <paramref name="actionId"/> on <paramref name="elementId"/>,
    /// writes: the command, or why not.
    /// </summary>
    public static DiagramPropertyChange Commit(DocumentEntry<LineDocument, WardleyMap> entry, IReadOnlyList<WardleyIdentityEntry> identities, string bodyPath, string elementId, string actionId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = WardleyDisl.Of(entry.Document, entry.Model, identities);
        var self = WardleyDisl.ElementOf(diagram, elementId);
        if (elementId.Length > 0 && self is null || EntryOf(diagram, self, actionId) is not { } found) return DiagramPropertyChange.Refused(DoesNotApply(actionId));

        WardleyStep step;
        switch (found)
        {
            case { Kind: "delete" } when self is not null:
                return Removal(bodyPath, diagram, self) is { } removal
                    ? DiagramPropertyChange.By(removal)
                    : DiagramPropertyChange.Refused(WardleyTransactionCommands.Unwritable("deletion"));

            case { Kind: "connect", Via: { } via } when self is not null:
                var connect = new DislTransaction([new DislChange.Connect(via, "", self.Id, value, new Dictionary<string, object?>(StringComparer.Ordinal))], [], null);
                return WardleyTransactionCommands.Of(bodyPath, diagram, connect) is { } connected
                    ? DiagramPropertyChange.By(connected)
                    : DiagramPropertyChange.Refused(WardleyTransactionCommands.Unwritable(via));

            case { Operation: { } operation }:
                {
                    var arguments = found.Arguments;
                    if (Unanswered(operation, arguments) is { } form && FormCommit.Request(Specification, form, self, Env, diagram)?.Fields is [var field])
                    {
                        object? answer = value;
                        if (field.Options is { } options)
                        {
                            var chosen = value.Length == 0 && options is [var only] ? only : options.FirstOrDefault(option => WardleyDisl.ChoiceIdOf(option.Value) == value);
                            if (chosen is null) return DiagramPropertyChange.Refused(DoesNotApply(actionId));
                            answer = chosen.Value;
                        }

                        arguments = Answered(arguments, field.Attribute, answer);
                    }

                    step = Ran(bodyPath, entry, identities, diagram, operation, self, arguments);
                    break;
                }

            default:
                return DiagramPropertyChange.Refused(DoesNotApply(actionId));
        }

        return step.Command is { } command ? DiagramPropertyChange.By(command) : DiagramPropertyChange.Refused(((ContextExecutionFailed)step.Answer!).Message);
    }

    /// <summary>
    /// What setting the row <paramref name="propertyId"/> of <paramref name="elementId"/> to <paramref name="value"/>
    /// writes: its form item at commit (DISL 0.3 §7.5), as the command that writes it, or why not.
    /// </summary>
    public static DiagramPropertyChange Change(DocumentEntry<LineDocument, WardleyMap> entry, IReadOnlyList<WardleyIdentityEntry> identities, string bodyPath, string elementId, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = WardleyDisl.Of(entry.Document, entry.Model, identities);
        if (WardleyDisl.ElementOf(diagram, elementId) is not { } element) return DiagramPropertyChange.Refused(Gone);
        // A row the form declares is one, shown or not: one hidden by its visible or its showAbsent meets the
        // definition's own refusals, as the evolution rows of an element that is not evolving do.
        var key = RowKeyOf(element, propertyId);
        if (!FormCommit.HasRow(Specification, element, key)) return DiagramPropertyChange.Refused($"'{propertyId}' is not a property of this element.");

        var working = WardleyDisl.Build(entry.Document, entry.Model, identities);
        var transaction = FormCommit.Row(Specification, working.ElementById(element.Id)!, key, value, Env, ids: DislIds.Of(Specification));
        if (!transaction.WasApplied) return DiagramPropertyChange.Refused(transaction.Refusal!);
        return WardleyTransactionCommands.Of(bodyPath, diagram, transaction) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused(WardleyTransactionCommands.Unwritable($"row '{key}'"));
    }

    /// <summary>
    /// The findings of <paramref name="map"/>, read from <paramref name="body"/> with <paramref name="passedOver"/> the
    /// lines its reader passed over, and <paramref name="identities"/> the ids the store holds beside it (none for a text it
    /// does not hold): the definition's constraints, in its <c>constraints.order</c> of codes, within a code by
    /// <see cref="FindingTypes"/> and then by line. <paramref name="beforeReader"/> are the findings the module makes itself,
    /// which the order puts before the reader's.
    /// </summary>
    /// <remarks>
    /// A finding is located at its line, except <c>wardley.duplicate-name</c>, which is at the first holder's id when the
    /// store holds one, and a finding about the whole map, which is nowhere. The reader's findings are the lines it passed
    /// over (<c>std.unreadableEntry</c>), and nothing else: the reader raises no other finding the tool gives.
    /// </remarks>
    public static IReadOnlyList<DiagramProblem> Problems(
        LineDocument body,
        WardleyMap map,
        IReadOnlyList<ParseProblem> passedOver,
        IReadOnlyList<WardleyIdentityEntry> identities,
        IReadOnlyList<DiagramProblem> beforeReader)
    {
        ArgumentNullException.ThrowIfNull(passedOver);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(beforeReader);
        var diagram = WardleyDisl.Of(body, map, identities);
        var reader = passedOver.Select(problem => DislReaderFinding.Unreadable(problem.Message, problem.Line + 1)).ToList();
        var findings = ConstraintEvaluator.Evaluate(Specification, diagram, new DislConstraintOptions(Env, reader));
        var codes = CodeOrder();
        var stored = identities.Select(entry => entry.Id).ToHashSet(StringComparer.Ordinal);

        var ordered = findings
            .Select((finding, arising) => (Finding: finding, Arising: arising))
            .OrderBy(entry => codes.GetValueOrDefault(entry.Finding.Code, int.MaxValue))
            .ThenBy(entry => TypeRank(diagram, entry.Finding))
            .ThenBy(entry => entry.Finding.Line ?? int.MaxValue)
            .ThenBy(entry => entry.Arising)
            .Select(entry => entry.Finding)
            .ToList();

        // The module's own findings go where the order puts their code; a reader's finding is always last.
        var readerFrom = ordered.FindIndex(finding => codes.GetValueOrDefault(finding.Code, int.MaxValue) > (beforeReader is [var first, ..] ? codes.GetValueOrDefault(first.RuleId, int.MaxValue) : int.MaxValue));
        var problems = ordered.Select(finding => Problem(finding, stored)).ToList();
        problems.InsertRange(readerFrom < 0 ? problems.Count : readerFrom, beforeReader);
        return problems;
    }

    /// <summary>The position of each code in the definition's <c>constraints.order</c>.</summary>
    private static Dictionary<string, int> CodeOrder() =>
        Specification.Json.TryGetProperty("constraints", out var constraints) && constraints.TryGetProperty("order", out var order) && order.ValueKind == JsonValueKind.Array
            ? order.EnumerateArray().Select((code, index) => (Code: code.GetString() ?? "", Index: index)).ToDictionary(entry => entry.Code, entry => entry.Index, StringComparer.Ordinal)
            : new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>Where the type of the element <paramref name="finding"/> is about comes in <see cref="FindingTypes"/>.</summary>
    private static int TypeRank(DislDiagram diagram, DislFinding finding)
    {
        if (finding.ElementIds is not [var id, ..] || diagram.ElementById(id) is not { } element) return FindingTypes.Length;
        var rank = Array.FindIndex(FindingTypes, types => types.Any(element.IsA));
        return rank < 0 ? FindingTypes.Length : rank;
    }

    private static DiagramProblem Problem(DislFinding finding, HashSet<string> stored)
    {
        DiagramProblemLocation? location = finding switch
        {
            { Code: DuplicateName, ElementIds: [var id, ..] } when stored.Contains(id) => new DiagramProblemElementLocation(id),
            { Line: { } line } => new DiagramProblemLineLocation((uint)line),
            _ => null,
        };
        return new DiagramProblem(DislDiagramDefinition.SeverityOf(finding), finding.Message, finding.Code, location);
    }

    private static DislEnv EnvOf(bool editable) => editable ? Env : ReadOnlyEnv;

    /// <summary>What a menu is opened on: the map for the empty id, an element of it, or nothing for any other id.</summary>
    private static DislMenuTarget? TargetOf(DislDiagram diagram, string elementId) =>
        elementId.Length == 0
            ? DislMenuTarget.Canvas(diagram)
            : WardleyDisl.ElementOf(diagram, elementId) is { } element
                ? DislMenuTarget.Element(element)
                : null;

    private static RemovalConfirmation? ConfirmationOf(DislElement element) =>
        DeletionPolicy.Confirmation(Specification, element, env: Env) is { } asked
            ? new RemovalConfirmation(asked.Title, asked.Message, asked.ConfirmLabel, asked.Danger)
            : null;

    /// <summary>
    /// The command that removes <paramref name="element"/>: what goes with it is its handler's, which takes the links and
    /// the evolution that name an element, as the definition's <c>delete-referencing</c> says and the runtime cannot yet
    /// run (<see cref="DeletionPolicy"/>).
    /// </summary>
    private static ICommand? Removal(string bodyPath, DislDiagram diagram, DislElement element) =>
        WardleyTransactionCommands.Of(bodyPath, diagram, new DislTransaction([new DislChange.Remove(element.Id)], [], null));

    /// <summary>
    /// Runs <paramref name="operation"/> on <paramref name="self"/> (the map for null) with <paramref name="arguments"/> as its
    /// parameters, on a model of its own, and the command that writes what it changed; or why not.
    /// </summary>
    private static WardleyStep Ran(
        string bodyPath,
        DocumentEntry<LineDocument, WardleyMap> entry,
        IReadOnlyList<WardleyIdentityEntry> identities,
        DislDiagram diagram,
        string operation,
        DislElement? self,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var working = WardleyDisl.Build(entry.Document, entry.Model, identities);
        var parameters = arguments.ToDictionary(
            argument => argument.Key,
            argument => argument.Value is DislElement element ? working.ElementById(element.Id) : argument.Value,
            StringComparer.Ordinal);
        var transaction = OperationInterpreter.Run(
            Specification,
            operation,
            working,
            self is null ? null : working.ElementById(self.Id),
            DislIds.Of(Specification),
            new DislInvocation(Parameters: parameters),
            Env);
        if (!transaction.WasApplied) return WardleyStep.Refused(transaction.Refusal!);
        return WardleyTransactionCommands.Of(bodyPath, diagram, transaction) is { } command
            ? WardleyStep.Writing(command)
            : WardleyStep.Refused(WardleyTransactionCommands.Unwritable(operation));
    }

    /// <summary>
    /// The entry <paramref name="actionId"/> names on <paramref name="self"/> (the map for null): one its menu offers, else
    /// the operation <c>x-wardley</c> gives that id when it is for <paramref name="self"/>; null for neither.
    /// </summary>
    private static DerivedMenuEntry? EntryOf(DislDiagram diagram, DislElement? self, string actionId)
    {
        var target = self is null ? DislMenuTarget.Canvas(diagram) : DislMenuTarget.Element(self);
        var offered = ContextMenuDerivation.Derive(Specification, target, Env, Definition.Ids)
            .SelectMany(group => group.Entries)
            .FirstOrDefault(candidate => candidate.Id == actionId);
        if (offered is not null) return offered;

        IReadOnlyList<string> types = self?.Type.Linearisation ?? ["diagram"];
        var operation = OperationNames().FirstOrDefault(name =>
            Definition.Ids.ActionId(types, "operation", name, NoArguments) == actionId && OperationInterpreter.AppliesTo(Specification, name, self));
        return operation is null
            ? null
            : new DerivedMenuEntry(actionId, "operation", operation, "", "", null, true, "", NoArguments);
    }

    private static readonly IReadOnlyDictionary<string, object?> NoArguments = new Dictionary<string, object?>(StringComparer.Ordinal);

    /// <summary>The parameters with <paramref name="name"/> answered by <paramref name="value"/>.</summary>
    private static Dictionary<string, object?> Answered(IReadOnlyDictionary<string, object?> arguments, string name, object? value) =>
        new(arguments, StringComparer.Ordinal) { [name] = value };

    /// <summary>
    /// The input a params form asks for: its title, icon, field and confirming label; over the element's label on the
    /// canvas when its one field edits that label and opens on it (wardley-map.md, gap 11).
    /// </summary>
    private static ContextInputRequest Input(FormRequest asked, FormRequestField field, DislElement? self)
    {
        var initial = field.Initial as string ?? "";
        var inline = self is { Type.LabelAttribute: { } label } && label == field.Attribute && self.ValueOf(label) as string == initial && initial.Length > 0;
        return new ContextInputRequest(asked.Label, asked.Icon ?? "", field.Label, initial, asked.SubmitLabel ?? "", inline ? self!.Id : "");
    }

    /// <summary>The params form <paramref name="operation"/> asks for its parameters with when <paramref name="arguments"/> do not answer them all; null otherwise.</summary>
    private static string? Unanswered(string operation, IReadOnlyDictionary<string, object?> arguments)
    {
        if (OperationJson(operation) is not { } declared) return null;
        var unanswered = declared.TryGetProperty("params", out var parameters) && parameters.ValueKind == JsonValueKind.Object
            && parameters.EnumerateObject().Any(parameter => !arguments.ContainsKey(parameter.Name));
        return unanswered ? JsonProperties.String(declared, "paramsForm") : null;
    }

    /// <summary>The first <c>unavailable</c> sentence of <paramref name="operation"/> written as text; null when it words none.</summary>
    private static string? Sentence(string operation) =>
        OperationJson(operation) is { } declared && declared.TryGetProperty("unavailable", out var reasons) && reasons.ValueKind == JsonValueKind.Array
            ? reasons.EnumerateArray().Select(reason => reason.ValueKind == JsonValueKind.Object ? JsonProperties.String(reason, "message") : null).FirstOrDefault(message => message is not null)
            : null;

    private static IEnumerable<string> OperationNames() =>
        Specification.Json.TryGetProperty("behavior", out var behavior) && behavior.TryGetProperty("operations", out var operations) && operations.ValueKind == JsonValueKind.Object
            ? operations.EnumerateObject().Select(operation => operation.Name)
            : [];

    private static JsonElement? OperationJson(string operation) =>
        Specification.Json.TryGetProperty("behavior", out var behavior)
        && behavior.TryGetProperty("operations", out var operations)
        && operations.ValueKind == JsonValueKind.Object
        && operations.TryGetProperty(operation, out var declared)
        && declared.ValueKind == JsonValueKind.Object
            ? declared
            : null;

    /// <summary>The key of the form item whose row is <paramref name="propertyId"/> on <paramref name="element"/>: the <c>x-wardley</c> property mapped to it, else the id itself.</summary>
    private static string RowKeyOf(DislElement element, string propertyId) =>
        Specification.Extensions.TryGetValue("x-wardley", out var block) && block.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object
            ? properties.EnumerateObject().Select(property => property.Name).FirstOrDefault(key => Definition.Ids.PropertyId(element.Type.Linearisation, key) == propertyId) ?? propertyId
            : propertyId;

    private static string DoesNotApply(string actionId) => $"'{actionId}' does not apply to this selection.";
}

/// <summary>What running an entry comes to: an answer for the user (a question, or a refusal), or a command to dispatch, with what to confirm first.</summary>
/// <param name="Answer">What the user is answered, or null when a command is dispatched.</param>
/// <param name="Command">The command to dispatch, or null.</param>
/// <param name="Confirmation">What to confirm before <paramref name="Command"/> runs, or null to run it at once.</param>
internal sealed record WardleyStep(ContextExecutionResult? Answer, ICommand? Command, RemovalConfirmation? Confirmation)
{
    public static WardleyStep Refused(string reason) => new(new ContextExecutionFailed(reason), null, null);

    public static WardleyStep Asking(ContextExecutionResult question) => new(question, null, null);

    public static WardleyStep Writing(ICommand command) => new(null, command, null);

    public static WardleyStep Removing(ICommand command, RemovalConfirmation? confirmation) => new(null, command, confirmation);
}
