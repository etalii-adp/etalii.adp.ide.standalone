using System.Globalization;
using System.Text.Json;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The causal loop diagram's bundled DISL definition (<c>definition/causal-loop-diagram.dis</c>, from
/// etalii-adp/etalii.adp), loaded once as a <see cref="DislDiagramDefinition"/>, and what the providers derive from it: the palette, the context
/// menus, the property rows and the deletion confirmation, mapped onto the host's types with the wire
/// ids of its <c>x-cld</c> block (causal-loop-disl-fbl Requirement 3.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the binding's reading</b>, built by <see cref="CausalLoopBody"/>. An element is found by
/// its wire id, which is its model id: the definition's natural ids with their prefixes. Of two elements
/// with one id the first is found, as the hand-written providers found the first.
/// </para>
/// <para>
/// <b>A document that could not be read offers nothing</b>, neither a menu nor a row: its emptiness is
/// not the document, so nothing may be written over it, and the host offers no action that would only refuse.
/// </para>
/// </remarks>
internal static class CausalLoopDefinition
{
    /// <summary>
    /// The definition, with its <c>x-cld</c> wire ids. Each operation that can be unavailable carries its
    /// sentence even while it is available, as the hand-written menu did.
    /// </summary>
    public static DislDiagramDefinition Definition { get; } = new(
        typeof(CausalLoopDefinition).Assembly,
        "causal-loop-diagram.dis",
        "x-cld",
        CausalLoopCycles.Plugins,
        ["makePositive", "makeNegative", "arrange"]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The id of the variable's label, which its inline rename edits.</summary>
    private const string VariableLabel = "display";

    /// <summary>The wire ids of <c>x-cld</c>.</summary>
    private static WireIdMap Ids => Definition.Ids;

    /// <summary>What <c>env</c> reads for a readable document: editable, with no viewpoint.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>What <c>env</c> reads for an edit: editable, with no viewpoint.</summary>
    public static DislEnv EditEnv => Env;

    /// <summary>The model of <paramref name="entry"/>: its body as the binding reads it.</summary>
    private static DislDiagram DiagramOf(DocumentEntry<CausalLoopBody, CausalLoopModel> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.Document.Disl.Diagram;
    }

    /// <summary>
    /// Writes the changes of <paramref name="transaction"/> into <paramref name="body"/>, in order, each
    /// as the FBL change <see cref="DislWrite"/> makes of it; the first refusal, the transaction's own or
    /// the binding's, stops it (causal-loop-disl-fbl Requirement 5.1).
    /// </summary>
    public static EditOutcome Apply(CausalLoopBody body, DislTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(transaction);
        if (!transaction.WasApplied) return EditOutcome.Refused(transaction.Refusal!);

        foreach (var change in transaction.Changes)
        {
            var edit = body.Change(DislWrite.ToFbl(Specification, change));
            if (!edit.WasApplied) return edit;
        }

        return EditOutcome.Applied;
    }

    /// <summary>
    /// Runs the definition's operation <paramref name="operation"/> on the element <paramref name="target"/> of
    /// <paramref name="body"/> (none for one on the diagram) with <paramref name="parameters"/>, then its
    /// <c>on: connect</c> hooks for every link it draws, and writes the result as one edit (causal-loop-disl-fbl
    /// Requirements 5.5, 7.2). A parameter whose type is a variable is answered with the variable's name.
    /// </summary>
    /// <remarks>
    /// An element or a variable the document does not hold is refused in the tool's sentence for that kind,
    /// as the commands these operations replace refused it.
    /// </remarks>
    public static EditOutcome Run(CausalLoopBody body, string operation, string? target, IReadOnlyDictionary<string, string> parameters)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(parameters);

        var diagram = body.Working();
        DislElement? self = null;
        if (target is not null && (self = ElementOf(diagram, target)) is null) return EditOutcome.Refused(NotHeld(target));

        var answered = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach ((string name, string value) in parameters)
        {
            if (ParameterType(operation, name) != "Variable")
            {
                answered[name] = value;
            }
            else if (VariableOf(diagram, value) is { } variable)
            {
                answered[name] = variable;
            }
            else
            {
                return EditOutcome.Refused(CausalLoopEdits.NoSuchVariable);
            }
        }

        var ids = new ClaimedLoopIds();
        var transaction = OperationInterpreter.Run(Specification, operation, diagram, self, ids, new DislInvocation(Parameters: answered), Env);
        if (!transaction.WasApplied) return EditOutcome.Refused(transaction.Refusal!);

        // The host runs the hooks of every relation an operation drew, in the same edit.
        var changes = new List<DislChange>(transaction.Changes);
        foreach (var connect in transaction.Changes.OfType<DislChange.Connect>())
        {
            var claimed = HookRunner.AfterConnect(Specification, diagram.ElementById(connect.Id)!, ids, Env);
            if (!claimed.WasApplied) return EditOutcome.Refused(claimed.Refusal!);
            changes.AddRange(claimed.Changes);
        }

        return Apply(body, new DislTransaction(changes, [], null));
    }

    /// <summary>
    /// The name and label of the variable the definition's <c>addVariableHere</c> declares next in
    /// <paramref name="body"/>: the lowest free number. Minted once, where the gesture happens, so a redo and the
    /// position stored for it name the same variable.
    /// </summary>
    public static (string Name, string Label) NewVariable(CausalLoopBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var created = OperationInterpreter.Run(Specification, "addVariableHere", body.Working(), null, new ClaimedLoopIds(), null, Env)
            .Changes.OfType<DislChange.Create>().Single();
        return ((string)created.Attributes["name"]!, (string)created.Attributes["label"]!);
    }

    /// <summary>
    /// The dialog the definition's params form <paramref name="form"/> asks for on <paramref name="elementId"/>
    /// in <paramref name="entry"/>: its title, icon, first field's label and initial value, and its confirming label.
    /// </summary>
    public static ContextInputRequest Request(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string form, string? elementId)
    {
        var diagram = DiagramOf(entry);
        var asked = FormCommit.Request(Specification, form, ElementOf(diagram, elementId), Env, diagram)
            ?? throw new InvalidOperationException($"The definition has no form '{form}'.");
        var field = asked.Fields[0];
        return new ContextInputRequest(asked.Label, asked.Icon ?? "", field.Label, field.Initial as string ?? "", asked.SubmitLabel ?? "");
    }

    /// <summary>Why the params form <paramref name="form"/> refuses <paramref name="value"/> for its item <paramref name="attribute"/> on <paramref name="elementId"/>; null when it takes it.</summary>
    public static string? Refusal(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string form, string attribute, string value, string? elementId)
    {
        var diagram = DiagramOf(entry);
        return FormCommit.Refusal(Specification, form, attribute, value, ElementOf(diagram, elementId), Env, diagram);
    }

    /// <summary>
    /// What committing <paramref name="value"/> to the row <paramref name="propertyId"/> of <paramref name="elementId"/>
    /// does: the definition's form item for that attribute at commit (DISL 0.3 7.5); null where the element has no
    /// attribute row of that id, as a computed row has none.
    /// </summary>
    public static DislTransaction? Row(CausalLoopBody body, string? elementId, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (ElementOf(body.Working(), elementId) is not { } element) return null;
        var attribute = element.Type.Attributes.Keys.FirstOrDefault(name => Ids.PropertyId(name) == propertyId);
        return attribute is null ? null : FormCommit.Row(Specification, element, attribute, value, Env);
    }

    /// <summary>Why the definition's change rule <paramref name="rule"/> refuses <paramref name="attribute"/> of <paramref name="elementId"/> becoming <paramref name="value"/>; null when it does not.</summary>
    public static string? ChangeRefusal(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId, string attribute, object? value, string rule) =>
        ElementOf(DiagramOf(entry), elementId) is { } element
            ? GestureConstraintEvaluator.Change(Specification, element, attribute, value, Env).FirstOrDefault(finding => finding.ConstraintId == rule)?.Message
            : null;

    /// <summary>What an inline rename of the variable <paramref name="elementId"/> to <paramref name="text"/> does: its label's <c>parse</c> (DISL 0.3 6.12); null for no variable.</summary>
    public static DislTransaction? Rename(CausalLoopBody body, string? elementId, string text)
    {
        ArgumentNullException.ThrowIfNull(body);
        return ElementOf(body.Working(), elementId) is { } element ? FormCommit.Label(Specification, element, VariableLabel, text, Env) : null;
    }

    /// <summary>The text the inline rename of <paramref name="elementId"/> opens on: its label's <c>editText</c>; empty for no variable.</summary>
    public static string RenameText(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId) =>
        ElementOf(DiagramOf(entry), elementId) is { } element ? FormCommit.EditText(Specification, element, VariableLabel, Env) ?? "" : "";

    /// <summary>The value <paramref name="transaction"/> sets on <paramref name="attribute"/>, and whether it sets one.</summary>
    public static bool Writes(DislTransaction transaction, string attribute, out object? value)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        foreach (var set in transaction.Changes.OfType<DislChange.Set>())
        {
            if (set.Attributes.TryGetValue(attribute, out value)) return true;
        }

        value = null;
        return false;
    }

    /// <summary>The declared type of the parameter <paramref name="name"/> of <paramref name="operation"/>; null for none.</summary>
    private static string? ParameterType(string operation, string name) =>
        Specification.Json.TryGetProperty("behavior", out var behavior)
        && behavior.TryGetProperty("operations", out var operations)
        && operations.TryGetProperty(operation, out var declared)
        && declared.TryGetProperty("params", out var parameters)
        && parameters.TryGetProperty(name, out var parameter)
        && parameter.TryGetProperty("type", out var type)
        && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    /// <summary>The tool's sentence for an element <paramref name="id"/> the document does not hold.</summary>
    private static string NotHeld(string id) =>
        id.StartsWith("link:", StringComparison.Ordinal) ? CausalLoopEdits.NoSuchLink
        : id.StartsWith("loop:", StringComparison.Ordinal) ? CausalLoopEdits.NoSuchLoop
        : CausalLoopEdits.NoSuchVariable;

    /// <summary>
    /// The findings of <paramref name="body"/> (causal-loop-disl-fbl Requirement 3.3): the definition's
    /// rules and built-ins, in its order, with a statement the binding does not read reported on its
    /// line in the tool's own sentence for it (<see cref="CausalLoopUnreadable"/>).
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(CausalLoopBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        IReadOnlyList<DislReaderFinding> unreadable =
        [
            .. body.UnreadableStatements.Select(statement => DislReaderFinding.Unreadable(CausalLoopUnreadable.SentenceFor(statement.Statement), statement.Line)),
        ];
        return ConstraintEvaluator.Evaluate(Specification, body.Disl.Diagram, new DislConstraintOptions(Env, unreadable));
    }

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable) return [];

        var diagram = DiagramOf(entry);
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.IsPlacement(elementId)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, VariableOf(diagram, from), VariableOf(diagram, to), "CausalLink")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: an element's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (!entry.IsUsable || ElementOf(DiagramOf(entry), elementId) is not { } element) return [];
        return [.. Definition.Rows(element, Env).Select(row => WithNumbersShownAsTheToolShowsThem(element, row))];
    }

    /// <summary>The property rows of the diagram itself in <paramref name="entry"/>, which an empty selection shows (etalii.adp spec 074, FR-025); none on a document that could not be read.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> DiagramRows(DocumentEntry<CausalLoopBody, CausalLoopModel> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsUsable ? [.. FormDerivation.Derive(Specification, DiagramOf(entry), Env, Ids).Select(DislDiagramDefinition.Row)] : [];
    }

    /// <summary>What committing <paramref name="value"/> to the row <paramref name="propertyId"/> of the diagram itself does: the definition's form item at commit.</summary>
    public static DiagramRowCommit DiagramRow(CausalLoopBody body, string propertyId, string value)
    {
        ArgumentNullException.ThrowIfNull(body);
        var attribute = Specification.Metamodel.DiagramAttributes.Keys.FirstOrDefault(name => Ids.PropertyId(name == "title" ? DiagramTitleRow : name) == propertyId);
        return attribute is null
            ? DiagramRowCommit.Refused($"'{propertyId}' is not a row of this diagram's form.")
            : FormCommit.DiagramRow(Specification, body.Working(), attribute == "title" ? DiagramTitleRow : attribute, value, Env);
    }

    /// <summary>The id the diagram's form gives its title's row, since a loop's title row has the plain one.</summary>
    private const string DiagramTitleRow = "diagramTitle";

    /// <summary>The confirmation removing the element <paramref name="elementId"/> asks for; null when it names none, or asks nothing.</summary>
    public static DislConfirmation? RemovalConfirmation(DocumentEntry<CausalLoopBody, CausalLoopModel> entry, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(DiagramOf(entry), elementId) is { } element ? DeletionPolicy.Confirmation(Specification, element, env: Env) : null;
    }

    /// <summary>The element <paramref name="id"/> names: the first in model order with that id; null for none.</summary>
    private static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return id is null ? null : diagram.ElementById(id);
    }

    /// <summary>The variable written with <paramref name="name"/>; null for none.</summary>
    public static DislElement? VariableOf(DislDiagram diagram, string name) =>
        diagram.NodesOfType("Variable").FirstOrDefault(variable => variable.Attributes.TryGetValue("name", out var written) && written as string == name);

    /// <summary>
    /// A row of a <c>number</c> attribute with its value in .NET's shortest round-trip form, <c>1E+21</c>,
    /// as the tool has always shown a weight. A row's text is CEL's <c>string(double)</c>, which is
    /// Go's <c>1e+21</c>; the two agree on ordinary values. The definition's companion records the
    /// difference (Other differences: numbers, finding B11); every other row is the definition's.
    /// </summary>
    private static ContextPropertyDefinition WithNumbersShownAsTheToolShowsThem(DislElement element, ContextPropertyDefinition row)
    {
        foreach ((string name, object? stored) in element.Attributes)
        {
            if (stored is double number && Ids.PropertyId(name) == row.Id) return row with { Value = number.ToString(CultureInfo.InvariantCulture) };
        }
        return row;
    }

    /// <summary>
    /// The ids of the loops a hook claims while it runs: unique on the working diagram, and never written,
    /// since a loop's id is its identifier, which the binding reads back.
    /// </summary>
    private sealed class ClaimedLoopIds : IIdSource
    {
        private int _next;

        public string Next(string type) => string.Create(CultureInfo.InvariantCulture, $"{type}:claimed-{++_next}");
    }
}
