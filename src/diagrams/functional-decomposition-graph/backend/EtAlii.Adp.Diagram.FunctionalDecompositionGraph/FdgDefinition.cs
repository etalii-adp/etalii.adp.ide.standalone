using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The functional decomposition graph's bundled DISL definition
/// (<c>definition/functional-decomposition-graph.dis</c>, from etalii-adp/etalii.adp), loaded once as a
/// <see cref="DislDiagramDefinition"/>, and what the providers derive from it: the palette, the context
/// menus, the property rows and the deletion confirmation, mapped onto the host's types with the wire
/// ids of its <c>x-fdg</c> block.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model is the parser's reading</b>, built by <see cref="FdgDisl"/> once per parsed model, so a
/// menu, a row and a confirmation read the same snapshot and no call builds it twice.
/// </para>
/// <para>
/// <b>An element is found by the id written in the document</b>, as <see cref="FdgModel"/> finds it:
/// the first element with it, else the first connection.
/// </para>
/// <para>
/// <b>Read-only changes nothing that is offered.</b> The tool has always offered its menus and rows on
/// an entry it could not read, and the commands refuse there; the menus and rows are derived editable
/// so that what the tool offers does not change with this step.
/// </para>
/// </remarks>
internal static class FdgDefinition
{
    /// <summary>The definition, with its <c>x-fdg</c> wire ids.</summary>
    public static DislDiagramDefinition Definition { get; } = new(typeof(FdgDefinition).Assembly, "functional-decomposition-graph.dis", "x-fdg");

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>What <c>env</c> reads: editable, as the tool has always been.</summary>
    private static DislEnv Env { get; } = new();

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="entry"/>: an element or a connection, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DocumentEntry<LineDocument, FdgModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var diagram = FdgDisl.Of(entry.Model);
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParsePlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out var from, out var to)
                    ? DislMenuTarget.Connection(diagram, ElementOf(diagram, from), ElementOf(diagram, to))
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="entry"/>: an element's or a connection's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DocumentEntry<LineDocument, FdgModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(FdgDisl.Of(entry.Model), elementId) is { } element ? Definition.Rows(element, Env) : [];
    }

    /// <summary>What removing <paramref name="elementId"/> asks first: how many connections go with it; null when it asks nothing.</summary>
    public static RemovalConfirmation? Confirmation(DocumentEntry<LineDocument, FdgModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ElementOf(FdgDisl.Of(entry.Model), elementId) is { } element
               && DeletionPolicy.Confirmation(Specification, element, env: Env) is { } asked
            ? new RemovalConfirmation(asked.Title, asked.Message, asked.ConfirmLabel, asked.Danger)
            : null;
    }

    /// <summary>
    /// The findings of <paramref name="model"/>: the definition's built-ins and rules over its model with the
    /// reader's sentences (<see cref="FdgReaderSentences"/>), held to the tool's findings by the stand-ins
    /// <see cref="FdgLinkChain"/> and <see cref="FdgFindingOrder"/>, each named for the gap or the ruling that
    /// keeps it. The ownership loops are <c>std.acyclic</c>'s, every elementary loop (ruled by the user,
    /// 2026-10-22).
    /// </summary>
    public static IReadOnlyList<DislFinding> Findings(FdgModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = FdgDisl.Of(model);
        var options = new DislConstraintOptions(Env, FdgReaderSentences.Of(model), WrittenId);
        var evaluated = ConstraintEvaluator.Evaluate(Specification, diagram, options);
        return FdgFindingOrder.Apply(FdgLinkChain.Apply(evaluated, diagram), model);
    }

    /// <summary>
    /// The definition's refusal of a <paramref name="relationType"/> connection from <paramref name="from"/> to
    /// <paramref name="to"/> in <paramref name="model"/> as it is: the first of the type, cardinality and cycle checks
    /// it fails, worded by the built-in's <c>refusal</c> (DISL 8.4); null when none refuses.
    /// </summary>
    public static string? ConnectRefusal(FdgModel model, string relationType, DislElement from, DislElement to)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(relationType);
        return GestureConstraintEvaluator.Connect(Specification, FdgDisl.Of(model), relationType, from, to, env: Env)?.Message;
    }

    /// <summary>The element <paramref name="id"/> names, never a connection, as a connect finds its ends; null for none.</summary>
    public static DislElement? EndOf(FdgModel model, string id)
    {
        ArgumentNullException.ThrowIfNull(model);
        return ElementOf(FdgDisl.Of(model), id) is { Type.IsRelation: false } element ? element : null;
    }

    /// <summary>The element or connection <paramref name="id"/> names, as <see cref="FdgModel"/> finds it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.Elements.FirstOrDefault(element => !element.Type.IsRelation && WrittenId(element) == id)
            ?? diagram.Elements.FirstOrDefault(element => element.Type.IsRelation && WrittenId(element) == id);
    }

    /// <summary>The id written in the document, the binding's <c>storedId</c>: empty for an entry without one.</summary>
    public static string WrittenId(DislElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.HostAttributes.TryGetValue(FdgDisl.StoredId, out var stored) ? stored as string ?? "" : "";
    }
}
