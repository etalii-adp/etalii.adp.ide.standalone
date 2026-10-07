using System.Runtime.CompilerServices;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// The dependency graph's bundled DISL definition (<c>definition/dependency-graph.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, and what the providers derive from it: the palette, the
/// context menus, the property rows, the node a placement adds and the confirmation a remove asks
/// for, mapped onto the host's types with the wire ids of its <c>x-dependencies</c> block.
/// Everything else stays in code, for the reasons below.
/// </summary>
/// <remarks>
/// <para>
/// <b>What stays in code, and why.</b> The findings (<see cref="DependencyGraphRuleSet"/>): the definition
/// states their codes and messages, but their order, an empty end that is no reference and a
/// self-dependency judged on the ids as written are the code's and DISL cannot say them
/// (<c>definition/dependency-graph.md</c>, section 3). Adding a node to the right or below: the
/// definition's operations create and <c>connect</c> in one transaction, and the DISL runtime cannot
/// run a <c>connect</c> action yet. The other edits, and every write, which are splices of the file's own
/// lines that no DISL change says how to make.
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
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(DependencyGraphDefinition).Assembly, "dependency-graph.dis"));

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
    public static WireIdMap Ids => LoadedIds.Value;

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
