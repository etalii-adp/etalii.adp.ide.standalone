using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Sankey;

/// <summary>
/// The Sankey diagram's bundled DISL definition (<c>definition/sankey.dis</c>, from etalii-adp/etalii.adp), loaded
/// once as a <see cref="DislDiagramDefinition"/> with <see cref="SankeyFunctions"/>, and what the providers derive
/// from it: the palette, the context menus, the property rows and what removing a node asks first, mapped onto the
/// host's types with the wire ids of its <c>x-sankey</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the reading's own</b>: <see cref="SankeyBody.Disl"/> of the body the <see cref="SankeyModel"/> was
/// read from, built by <see cref="DislModelBuilder"/> from the binding's reading, so a menu and a row read the same
/// snapshot and no call builds it twice.
/// </para>
/// <para>
/// <b>A menu target</b> is an element, the empty canvas (a placement), or a connect gesture between the nodes it
/// names; the empty id and an id the document does not hold have no menu, as before.
/// </para>
/// <para>
/// <b>An element is found by the id written in the document</b>, as <see cref="SankeyModel"/> finds it: the first node
/// with it, else the first flow. A node without an id, or a later holder of one, is addressed by its place (FBL
/// section 5.3), so it is never the one an id finds.
/// </para>
/// <para>
/// <b>Arrange, thicker, thinner and decrease carry their sentence even while they are available</b>, as the
/// hand-written menus did.
/// </para>
/// <para>
/// <b>Not read-only</b> (<c>env.readOnly</c> false): the hand-written menus and rows offered everything on an entry that
/// could not be read, and the commands refuse there; whether that should change is the specification's question, not
/// this derivation's.
/// </para>
/// </remarks>
internal static class SankeyDefinition
{
    /// <summary>The definition, with its <c>x-sankey</c> wire ids.</summary>
    private static DislDiagramDefinition Definition { get; } = new(
        typeof(SankeyDefinition).Assembly,
        "sankey.dis",
        "x-sankey",
        SankeyFunctions.Plugins,
        ["arrange", "thicker", "thinner", "decreaseValue"]);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => Definition.Toolbox;

    /// <summary>What <c>env</c> reads for a menu and a form: editable, in no viewpoint.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: an element, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<LineDocument, SankeyModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = DislOf(entry.Model).Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, NodeOf(diagram, from), NodeOf(diagram, to), "Flow")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: a node's or a flow's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, SankeyModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(DislOf(entry.Model).Diagram, elementId) is { } element ? Definition.Rows(element, Env) : [];
    }

    /// <summary>
    /// What removing the element <paramref name="elementId"/> asks first, the definition's <c>behavior.deletion</c>: a node with a flow
    /// written to or from it; none for a node without one, for a flow, and for anything else.
    /// </summary>
    public static RemovalConfirmation? RemovalConfirmation(SankeyModel model, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(DislOf(model).Diagram, elementId) is { } element && DeletionPolicy.Confirmation(Specification, element, env: Env) is { } asked
            ? new RemovalConfirmation(asked.Title, asked.Message, asked.ConfirmLabel, asked.Danger)
            : null;
    }

    /// <summary>
    /// The findings of <paramref name="outcome"/>, the definition's (<see cref="ConstraintEvaluator"/>) in the order of
    /// their lines (<see cref="SankeyFindingOrder"/>). What the reader could not read, and a node it read without an id,
    /// are handed in as the reader's findings, which the definition gives their code, severity and sentence.
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(ParseOutcome<SankeyModel> outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        var options = new DislConstraintOptions(Env, SankeyReader.Findings(outcome), WrittenId);
        return SankeyFindingOrder.ByLine(ConstraintEvaluator.Evaluate(Specification, DislOf(outcome.Model).Diagram, options));
    }

    /// <summary>The element <paramref name="id"/> names, as <see cref="SankeyModel"/> finds it: the first node with it, else the first flow; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string? id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return NodeOf(diagram, id) ?? (id is { Length: > 0 } ? diagram.RelationsOfType("Flow").FirstOrDefault(flow => flow.Id == id) : null);
    }

    private static DislElement? NodeOf(DislDiagram diagram, string? id) =>
        id is { Length: > 0 } ? diagram.NodesOfType("Node").FirstOrDefault(node => node.Id == id) : null;

    /// <summary>The DISL model of the body <paramref name="model"/> was read from; an empty diagram's for a model no body was read into.</summary>
    private static DislModel DislOf(SankeyModel model) => (SankeyReader.BodyOf(model) ?? SankeyBody.Parse("")).Disl;

    /// <summary>The id an element is written with: a node's <c>storedId</c>, a flow's id.</summary>
    private static string WrittenId(DislElement element) => element.Type.IsRelation ? element.Id : SankeyFunctions.WrittenId(element);
}
