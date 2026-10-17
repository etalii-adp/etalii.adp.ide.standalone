using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The dependency graph's bundled DISL definition (<c>definition/dependency-graph.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what the providers derive from it: the palette, the
/// context menus, the property rows, the node a placement adds and the confirmation a remove asks
/// for, mapped onto the host's types with the wire ids of its <c>x-dependencies</c> block; adding a
/// node to the right or below (the <c>addRight</c> and <c>addBelow</c> operations, run by the DISL
/// runtime with their <c>connect</c>) and a dependency dragged onto empty canvas (its tool's
/// <c>createTarget</c> or <c>createSource</c>), each mapped onto the module's command; and the findings
/// (<see cref="DependencyGraphFindings"/>). Everything else stays in code, for the reasons below.
/// </summary>
/// <remarks>
/// <para>
/// <b>What stays in code, and why.</b> The order the findings are reported in, which
/// <c>constraints.order</c> cannot state (<c>definition/dependency-graph.md</c>, section 3), and the ids
/// and ends as written, which the definition's <c>writtenEnd</c> function reads from the binding's host
/// attributes. Every write, which is still a splice of the file's own lines over the parser's ranges, so
/// a DISL transaction is mapped onto the command that makes that splice.
/// </para>
/// <para>
/// <b>The model is the body's own</b>: <see cref="DependencyGraphBody.Disl"/>, built by
/// <see cref="DislModelBuilder"/> from the binding's reading, so a menu and a row read the same snapshot.
/// An operation runs on a model of its own (<see cref="DependencyGraphBody.Working"/>).
/// </para>
/// <para>
/// <b>An element is found by the id written in the document</b>, the binding's <c>storedId</c>: the
/// first node with it, else the first dependency. A later entry reusing an id, or one without an id,
/// is told apart by FBL by its place; the client knows it by what is written.
/// </para>
/// <para>
/// <b>Nothing is gated on read-only</b>: the menus and rows of a graph that could not be read are the
/// same as of one that could, and its edits refuse instead, as they always have.
/// </para>
/// </remarks>
internal static class DependencyGraphDefinition
{
    private static readonly DislEnv Env = new();

    /// <summary>The bundled definition, with the palette, the menus and the rows every module derives the same way.</summary>
    public static DislDiagramDefinition Definition { get; } = new(typeof(DependencyGraphDefinition).Assembly, "dependency-graph.dis", "x-dependencies", DependencyGraphFindings.Plugins);

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Definition.Specification;

    /// <summary>The wire ids of <c>x-dependencies</c>.</summary>
    private static WireIdMap Ids => Definition.Ids;

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="body"/>: a node, a dependency, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DependencyGraphBody body, string elementId)
    {
        ArgumentNullException.ThrowIfNull(body);
        var diagram = body.Disl.Diagram;
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : GestureIds.TryParseRowPlacement(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : GestureIds.TryParseRelation(elementId, out _, out _)
                    ? DislMenuTarget.Connection(diagram, null, null, "DependsOn")
                    : null;
        return target is null ? [] : Definition.Menus(target, Env);
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="body"/>: a node's or a dependency's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DependencyGraphBody body, string elementId)
    {
        ArgumentNullException.ThrowIfNull(body);
        if (ElementOf(body.Disl.Diagram, elementId) is not { } element) return [];

        return As(Definition.Rows(element, Env), AsWritten(element));
    }

    /// <summary>
    /// The node the definition's <c>addNodeHere</c> adds at a placement's point - <paramref name="x"/> and
    /// <paramref name="row"/> - under a new ShortGuid: its label and its place as the operation computes
    /// them, as a command; or, when the operation refuses or adds anything else, why not.
    /// </summary>
    public static (AddDependencyGraphElementCommand? Command, string Refusal) NodeHere(DependencyGraphBody body, string bodyPath, double x, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        var transaction = OperationInterpreter.Run(
            Specification,
            "addNodeHere",
            body.Working(),
            null,
            DislIds.Fixed(ShortGuid.NewShortGuid().ToString()),
            new DislInvocation(Position: new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = x, ["y"] = (double)row }),
            Env);
        if (!transaction.WasApplied) return (null, transaction.Refusal!);
        if (transaction.Changes is not [DislChange.Create { Type: "Node" } create]
            || create.Attributes.GetValueOrDefault("label") is not string label
            || create.Attributes.GetValueOrDefault("x") is not double at
            || create.Attributes.GetValueOrDefault("row") is not long on)
        {
            return (null, "The definition's addNodeHere does not add one node with a label, an x and a row.");
        }

        return (new AddDependencyGraphElementCommand(bodyPath, create.Id, label, at, checked((int)on)), "");
    }

    /// <summary>
    /// The node <paramref name="operation"/> (<c>addRight</c> or <c>addBelow</c>) adds beside
    /// <paramref name="elementId"/>, and the dependency on it, as the definition computes them: one command,
    /// under two new ids; or, when the operation refuses or makes anything else, why not.
    /// </summary>
    public static (AddConnectedDependencyGraphElementCommand? Command, string Refusal) Grown(DependencyGraphBody body, string bodyPath, string operation, string elementId)
    {
        ArgumentNullException.ThrowIfNull(body);
        var diagram = body.Working();
        if (NodeOf(diagram, elementId) is not { } self) return (null, "That is no longer in this graph.");

        var transaction = OperationInterpreter.Run(Specification, operation, diagram, self, NewIds(), null, Env);
        return Related(transaction, bodyPath, elementId, operation, newElementIsSource: false, self);
    }

    /// <summary>
    /// A dependency gesture released on empty canvas at <paramref name="x"/> and <paramref name="row"/>: the
    /// node the definition's <c>dependsOn</c> tool creates there, as the gesture's <paramref name="newEnd"/>
    /// (<c>source</c> from a left anchor, <c>target</c> from a right one), and the dependency between it and
    /// <paramref name="elementId"/>, as one command; or why not.
    /// </summary>
    public static (AddConnectedDependencyGraphElementCommand? Command, string Refusal) RelatedHere(DependencyGraphBody body, string bodyPath, string elementId, string newEnd, double x, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        var diagram = body.Working();
        if (NodeOf(diagram, elementId) is not { } existing) return (null, "That is no longer in this graph.");

        var transaction = OperationInterpreter.ConnectToNew(
            Specification,
            "DependsOn",
            diagram,
            existing,
            newEnd,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = x, ["y"] = (double)row },
            NewIds(),
            Env);
        return Related(transaction, bodyPath, elementId, "dependsOn tool", newElementIsSource: newEnd == "source", existing);
    }

    /// <summary>Two ids for a node and its dependency, minted in that order as the hand-written commands did.</summary>
    private static IIdSource NewIds() => DislIds.Fixed(ShortGuid.NewShortGuid().ToString(), ShortGuid.NewShortGuid().ToString());

    /// <summary>A transaction that creates one node and relates it to <paramref name="existing"/>, as the command that writes both.</summary>
    private static (AddConnectedDependencyGraphElementCommand? Command, string Refusal) Related(DislTransaction transaction, string bodyPath, string elementId, string what, bool newElementIsSource, DislElement existing)
    {
        if (!transaction.WasApplied) return (null, transaction.Refusal!);
        if (transaction.Changes is not [DislChange.Create { Type: "Node" } created, DislChange.Connect { Type: "DependsOn" } relation]
            || (newElementIsSource ? relation.SourceId : relation.TargetId, newElementIsSource ? relation.TargetId : relation.SourceId) != (created.Id, existing.Id)
            || created.Attributes.GetValueOrDefault("label") is not string label
            || created.Attributes.GetValueOrDefault("x") is not double x
            || created.Attributes.GetValueOrDefault("row") is not long row)
        {
            return (null, $"The definition's {what} does not add one node with a label, an x and a row, related to this one.");
        }

        return (new AddConnectedDependencyGraphElementCommand(bodyPath, elementId, created.Id, relation.Id, x, checked((int)row), newElementIsSource, label), "");
    }

    /// <summary>The element <paramref name="id"/> names, by the id written in the document: the first node with it, else the first dependency; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id) =>
        NodeOf(diagram, id) ?? diagram.RelationsOfType("DependsOn").FirstOrDefault(relation => WrittenId(relation) == id);

    /// <summary>The first node written with <paramref name="id"/>; null for none.</summary>
    private static DislElement? NodeOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.NodesOfType("Node").FirstOrDefault(node => WrittenId(node) == id);
    }

    /// <summary>The id written in the document, the binding's <c>storedId</c>: empty for an entry without one.</summary>
    public static string WrittenId(DislElement element) => HostText(element, "storedId");

    /// <summary>A host attribute of <paramref name="element"/> as the text it was read as; empty when it was not written.</summary>
    internal static string HostText(DislElement element, string name)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.HostAttributes.TryGetValue(name, out var value) ? value as string ?? "" : "";
    }

    /// <summary>
    /// The values the code has always written itself, by row id. A node's X is the number as the file
    /// writes it (<see cref="DslNumber.Format(double)"/>, at most four decimals), which
    /// CEL's <c>string(double)</c> writes in exponent form from 1e+06 on. A dependency's end that resolves
    /// to no node is the id the file names: DISL hands CEL such an end as null and gives it no way to read
    /// the id it was written with (§4.9). Every other value is the definition's.
    /// </summary>
    private static Dictionary<string, string> AsWritten(DislElement element)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.IsA("Node"))
        {
            if (element.ValueOf("x") is double x) values[Ids.PropertyId("x")] = DslNumber.Format(x);
        }
        else if (element.Source is null || element.Target is null)
        {
            values[Ids.PropertyId("from")] = HostText(element, "writtenFrom");
            values[Ids.PropertyId("to")] = HostText(element, "writtenTo");
        }

        return values;
    }

    private static IReadOnlyList<ContextPropertyDefinition> As(IReadOnlyList<ContextPropertyDefinition> rows, Dictionary<string, string> values) =>
        values.Count == 0 ? rows : [.. rows.Select(row => values.TryGetValue(row.Id, out var value) ? row with { Value = value } : row)];
}
