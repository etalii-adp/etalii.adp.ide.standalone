using System.Globalization;
using EtAlii.Adp.Context;
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
    /// Draws the link from the variable <paramref name="from"/> to <paramref name="to"/>, stating
    /// <paramref name="polarity"/> (none for null), and writes it with every loop the definition's
    /// <c>on: connect</c> hook claims for it, as one edit (causal-loop-disl-fbl Requirement 5.5). Both
    /// variables are declared and the link is not yet stated; the caller checked.
    /// </summary>
    public static EditOutcome Draw(CausalLoopBody body, string from, string to, string? polarity)
    {
        ArgumentNullException.ThrowIfNull(body);
        var diagram = body.Working();
        var cause = VariableOf(diagram, from) ?? throw new ArgumentException($"No variable is named '{from}'.", nameof(from));
        var effect = VariableOf(diagram, to) ?? throw new ArgumentException($"No variable is named '{to}'.", nameof(to));

        // The cause and effect names are the binding's reading of the ends, which the hook's cycles are
        // keyed by; only the polarity is written.
        var written = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (polarity is not null) written["polarity"] = polarity;
        var read = new Dictionary<string, object?>(written, StringComparer.Ordinal) { ["causeName"] = from, ["effectName"] = to };
        var id = CausalLoopEdits.LinkId(from, to);
        var link = diagram.AddRelation("CausalLink", id, cause, effect, read);

        var claimed = HookRunner.AfterConnect(Specification, link, new ClaimedLoopIds(), Env);
        return claimed.WasApplied
            ? Apply(body, new DislTransaction([new DislChange.Connect("CausalLink", id, cause.Id, effect.Id, written), .. claimed.Changes], [], null))
            : EditOutcome.Refused(claimed.Refusal!);
    }

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
