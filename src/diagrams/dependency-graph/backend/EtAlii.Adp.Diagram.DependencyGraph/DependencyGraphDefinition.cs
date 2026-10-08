using System.Runtime.CompilerServices;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
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
/// and ends as written, which the definition's <c>writtenEnd</c> function reads from the module's reading.
/// Every write, which is a splice of the file's own lines that no DISL change says how to make, so a
/// DISL transaction is mapped onto the command that makes that splice.
/// </para>
/// <para>
/// <b>The model is built from the module's own reading</b> (<see cref="DependencyGraphParser"/>), one
/// DISL element per entry in the order the reader produced them, and kept with that reading so a
/// menu and a row read the same snapshot. The reader stays because every write is a splice at the
/// line ranges it records and every finding is placed at one; a second reader of the same bytes, an
/// FBL binding, would be a copy to keep in agreement with nothing gained.
/// </para>
/// <para>
/// <b>An element is found by its id as <see cref="DependencyGraphEdits"/> finds it</b>: the first node
/// with it, else the first dependency. A dependency's end resolves to the first node with the id it
/// names; an end that names nothing, or no node, is left unresolved.
/// </para>
/// <para>
/// <b>Nothing is gated on read-only</b>: the menus and rows of a graph that could not be read are the
/// same as of one that could, and its edits refuse instead, as they always have.
/// </para>
/// </remarks>
internal static class DependencyGraphDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(DependencyGraphDefinition).Assembly, "dependency-graph.dis", DependencyGraphFindings.Plugins()));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-dependencies"));

    private static readonly Lazy<IReadOnlyList<ToolboxItemDefinition>> LoadedToolbox = new(() =>
    [
        .. ToolboxDerivation.Derive(Specification, Ids)
            .Select(tool => new ToolboxItemDefinition(tool.Id, tool.Label, tool.Icon, tool.Description, tool.DropActionId ?? "")),
    ]);

    private static readonly ConditionalWeakTable<DependencyGraphModel, DislDiagram> Built = [];

    private static readonly DislEnv Env = new();

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-dependencies</c>.</summary>
    private static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The palette.</summary>
    public static IReadOnlyList<ToolboxItemDefinition> Toolbox => LoadedToolbox.Value;

    /// <summary>The DISL model of <paramref name="model"/>, built once per reading.</summary>
    public static DislDiagram Disl(DependencyGraphModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Built.GetValue(model, Build);
    }

    /// <summary>The context menu of <paramref name="elementId"/> in <paramref name="model"/>: a node, a dependency, a placement, a connect gesture; none for anything else.</summary>
    public static IReadOnlyList<ContextActionGroupDefinition> Menus(DependencyGraphModel model, string elementId)
    {
        var diagram = Disl(model);
        DislMenuTarget? target = ElementOf(diagram, elementId) is { } element
            ? DislMenuTarget.Element(element)
            : DependencyGraphNewPlacement.TryParse(elementId, out _, out _)
                ? DislMenuTarget.Canvas(diagram)
                : DependencyGraphRelationGesture.TryParse(elementId, out _, out _)
                    ? DislMenuTarget.Connection(diagram, null, null, "DependsOn")
                    : null;
        if (target is null) return [];

        return
        [
            .. ContextMenuDerivation.Derive(Specification, target, Env, Ids)
                .Select(group => new ContextActionGroupDefinition([.. group.Entries.Select(Action)])),
        ];
    }

    /// <summary>The property rows of <paramref name="elementId"/> in <paramref name="model"/>: a node's or a dependency's form; none for anything else.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(DependencyGraphModel model, string elementId)
    {
        var diagram = Disl(model);
        if (ElementOf(diagram, elementId) is not { } element) return [];

        var rows = FormDerivation.Derive(Specification, element, Env, Ids).Select(Row).ToList();
        return As(rows, AsWritten(model, element));
    }

    /// <summary>
    /// The node the definition's <c>addNodeHere</c> adds at a placement's point - <paramref name="x"/> and
    /// <paramref name="row"/> - under a new ShortGuid: its label and its place as the operation computes
    /// them, as a command; or, when the operation refuses or adds anything else, why not.
    /// </summary>
    public static (AddDependencyGraphElementCommand? Command, string Refusal) NodeHere(DependencyGraphModel model, string body, double x, int row)
    {
        ArgumentNullException.ThrowIfNull(model);
        var transaction = OperationInterpreter.Run(
            Specification,
            "addNodeHere",
            Build(model),
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

        return (new AddDependencyGraphElementCommand(body, create.Id, label, at, checked((int)on)), "");
    }

    /// <summary>
    /// The node <paramref name="operation"/> (<c>addRight</c> or <c>addBelow</c>) adds beside
    /// <paramref name="elementId"/>, and the dependency on it, as the definition computes them: one command,
    /// under two new ids; or, when the operation refuses or makes anything else, why not.
    /// </summary>
    public static (AddConnectedDependencyGraphElementCommand? Command, string Refusal) Grown(DependencyGraphModel model, string body, string operation, string elementId)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = Build(model);
        if (diagram.NodesOfType("Node").FirstOrDefault(node => node.Id == elementId) is not { } self) return (null, "That is no longer in this graph.");

        var transaction = OperationInterpreter.Run(Specification, operation, diagram, self, NewIds(), null, Env);
        return Related(transaction, body, elementId, operation, newElementIsSource: false, self);
    }

    /// <summary>
    /// A dependency gesture released on empty canvas at <paramref name="x"/> and <paramref name="row"/>: the
    /// node the definition's <c>dependsOn</c> tool creates there, as the gesture's <paramref name="newEnd"/>
    /// (<c>source</c> from a left anchor, <c>target</c> from a right one), and the dependency between it and
    /// <paramref name="elementId"/>, as one command; or why not.
    /// </summary>
    public static (AddConnectedDependencyGraphElementCommand? Command, string Refusal) RelatedHere(DependencyGraphModel model, string body, string elementId, string newEnd, double x, int row)
    {
        ArgumentNullException.ThrowIfNull(model);
        var diagram = Build(model);
        if (diagram.NodesOfType("Node").FirstOrDefault(node => node.Id == elementId) is not { } existing) return (null, "That is no longer in this graph.");

        var transaction = OperationInterpreter.ConnectToNew(
            Specification,
            "DependsOn",
            diagram,
            existing,
            newEnd,
            new Dictionary<string, object?>(StringComparer.Ordinal) { ["x"] = x, ["y"] = (double)row },
            NewIds(),
            Env);
        return Related(transaction, body, elementId, "dependsOn tool", newElementIsSource: newEnd == "source", existing);
    }

    /// <summary>Two ids for a node and its dependency, minted in that order as the hand-written commands did.</summary>
    private static IIdSource NewIds() => DislIds.Fixed(ShortGuid.NewShortGuid().ToString(), ShortGuid.NewShortGuid().ToString());

    /// <summary>A transaction that creates one node and relates it to <paramref name="existing"/>, as the command that writes both.</summary>
    private static (AddConnectedDependencyGraphElementCommand? Command, string Refusal) Related(DislTransaction transaction, string body, string elementId, string what, bool newElementIsSource, DislElement existing)
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

        return (new AddConnectedDependencyGraphElementCommand(body, elementId, created.Id, relation.Id, x, checked((int)row), newElementIsSource, label), "");
    }

    /// <summary>The element <paramref name="id"/> names, as <see cref="DependencyGraphEdits"/> finds it; null for none.</summary>
    public static DislElement? ElementOf(DislDiagram diagram, string id)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        return diagram.NodesOfType("Node").FirstOrDefault(node => node.Id == id)
            ?? diagram.RelationsOfType("DependsOn").FirstOrDefault(relation => relation.Id == id);
    }

    private static DislDiagram Build(DependencyGraphModel model)
    {
        var diagram = new DislDiagram(Specification);
        var first = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var element in model.Elements)
        {
            var node = diagram.AddNode("Node", element.Id, new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["label"] = element.Label,
                ["x"] = element.X,
                ["row"] = (long)element.Row,
            });
            first.TryAdd(element.Id, node);
        }

        foreach (var relation in model.Relations)
        {
            diagram.AddRelation(
                "DependsOn",
                relation.Id,
                End(relation.From),
                End(relation.To),
                new Dictionary<string, object?>(StringComparer.Ordinal) { ["label"] = relation.Label });
        }

        return diagram;

        DislElement? End(string id) => id.Length > 0 ? first.GetValueOrDefault(id) : null;
    }

    private static ContextActionDefinition Action(DerivedMenuEntry entry) => new(
        entry.Id,
        entry.Label,
        entry.Icon,
        entry.Shortcut is { } key ? new ContextShortcutDefinition(key.Key, key.Ctrl, key.Shift, key.Alt, key.Meta) : null,
        entry.Available,
        entry.UnavailableReason);

    /// <summary>A row as the grid draws it: every row of this type is a line.</summary>
    private static ContextPropertyDefinition Row(DerivedRow row) =>
        new(row.Id, row.Label, row.Value, ContextPropertyEditor.Line, row.ReadOnlyReason, row.Group);

    /// <summary>
    /// The values the code has always written itself, by row id. A node's X is the number as the file
    /// writes it (<see cref="DependencyGraphWriter.Number"/>, .NET's shortest invariant text), which
    /// CEL's <c>string(double)</c> writes in exponent form from 1e+06 on. A dependency's end that resolves
    /// to no node is the id the file names: DISL hands CEL such an end as null and gives it no way to read
    /// the id it was written with (§4.9). Every other value is the definition's.
    /// </summary>
    private static Dictionary<string, string> AsWritten(DependencyGraphModel model, DislElement element)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (element.IsA("Node"))
        {
            if (element.ValueOf("x") is double x) values[Ids.PropertyId("x")] = DependencyGraphWriter.Number(x);
        }
        else if (element.Source is null || element.Target is null)
        {
            var relation = model.Relations[element.Diagram.Relations.ToList().IndexOf(element)];
            values[Ids.PropertyId("from")] = relation.From;
            values[Ids.PropertyId("to")] = relation.To;
        }

        return values;
    }

    private static List<ContextPropertyDefinition> As(List<ContextPropertyDefinition> rows, Dictionary<string, string> values) =>
        values.Count == 0 ? rows : [.. rows.Select(row => values.TryGetValue(row.Id, out var value) ? row with { Value = value } : row)];
}
