using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The behavior model's bundled DISL definition (<c>definition/agent-behavior-modelling.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what the providers derive from it: the palette, the
/// context menus and the property rows, mapped onto the host's types with the wire ids of its
/// <c>x-abm</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>A node's DISL type is the one <c>x-abm.types</c> maps to its kind</b>: <c>Sequence</c> for
/// <c>sequence</c>, <c>Do</c> for <c>action</c>, and so on for the eleven. The persistence plugin reads
/// and writes the types through these two maps, so the Markdown's keywords stay the module's.
/// </para>
/// <para>
/// <b>The model is the document's own</b>: <see cref="AbmBody.Disl"/>, kept with the bytes it was built
/// from. A node's id is its place, which the definition derives and the module addresses it by.
/// </para>
/// <para>
/// <b>The code wins where the two would differ</b>, and two host choices stay here: <c>env.readOnly</c>
/// is always false, because the module never offered a read-only menu; and Arrange carries its sentence
/// even while it is available. A shortcut is sent as a keypress reports it - the definition's
/// <c>Alt+Up</c> is the key <c>ArrowUp</c> with Alt held - where the module once passed the text whole,
/// which matched no keypress (spec 025, T033).
/// </para>
/// </remarks>
internal static class AbmDefinition
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedKindOfType = new(() =>
        Ids.Types.Where(pair => AbmNodeKinds.IsKnown(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedTypeOfKind = new(() =>
        KindOfType.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal));

    /// <summary>
    /// The bundled definition, with the palette, the menus and the rows every module derives the same way.
    /// Arrange and Arrange from here carry their sentence even while they are available, as the
    /// hand-written menu did.
    /// </summary>
    public static DislDiagramDefinition Definition { get; } = new(typeof(AbmDefinition).Assembly, "agent-behavior-modelling.dis", "x-abm", standingOperations: ["arrange", "arrangeFromNode"]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The wire ids of <c>x-abm</c>.</summary>
    private static WireIdMap Ids => Definition.Ids;

    /// <summary>The kind of each node type: <c>Do</c> is <c>action</c>.</summary>
    public static IReadOnlyDictionary<string, string> KindOfType => LoadedKindOfType.Value;

    /// <summary>The node type of each kind: <c>action</c> is <c>Do</c>.</summary>
    public static IReadOnlyDictionary<string, string> TypeOfKind => LoadedTypeOfKind.Value;

    /// <summary>What <c>env</c> reads: editable, always, as the module's menus and rows have always been.</summary>
    public static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: a node, a placement, a parent-line gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<AbmBody, AbmModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = entry.Document.Disl.Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, ElementOf(diagram, from), ElementOf(diagram, to), "Child")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: a node's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<AbmBody, AbmModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(entry.Document.Disl.Diagram, elementId) is { } element
            ? Definition.Rows(element, Env)
            : [];
    }

    /// <summary>
    /// The id a node an operation creates has in the transaction's working model. A node's id is its place,
    /// which the definition derives once the document is read again; nothing is written to name it.
    /// </summary>
    public static IIdSource NewIds => DislIds.Fixed("new");

    /// <summary>Writes <paramref name="transaction"/>'s changes to <paramref name="document"/> in order, or gives its refusal.</summary>
    public static EditOutcome Apply(AbmBody document, DislTransaction transaction) =>
        Apply(document, transaction, change => change);

    /// <summary>
    /// Writes <paramref name="transaction"/>'s changes to <paramref name="document"/> in order, each as
    /// <paramref name="written"/> makes it from the FBL change the definition writes, or gives its refusal.
    /// </summary>
    public static EditOutcome Apply(AbmBody document, DislTransaction transaction, Func<ModelChange, ModelChange> written)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(written);
        if (!transaction.WasApplied) return EditOutcome.Refused(transaction.Refusal!);

        foreach (var change in transaction.Changes)
        {
            var edit = document.Change(written(DislWrite.ToFbl(Specification, change)));
            if (!edit.WasApplied) return edit;
        }
        return EditOutcome.Applied;
    }

    /// <summary>The node <paramref name="id"/> names - its place - or null.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.Nodes.FirstOrDefault(node => node.Id == id);
    }
}
