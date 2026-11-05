using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Layout;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;
using Google.Protobuf;
using JetBrains.Annotations;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// Turns the definition's model of a chart and its boxes into the core element and delta vocabulary -
/// without extending that vocabulary by a single field (Requirement 4.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole diagram is delivered at once - no viewport filtering.</b> A chart is bounded:
/// dozens of nodes even for the prometheus example, so there is nothing to virtualize. (The
/// Ansible module filters by viewport because an Ansible estate is unbounded; a chart is not -
/// the design records the fork.)
/// </para>
/// <para>
/// <b>Everything is the definition's model's</b> (helm-chart-disl-fbl, tasks 6 and 9): every node and its
/// payload are read from the model's attributes, derived ones included (a chart's <c>legacy</c>, a values
/// layer's <c>hasGlobal</c>, a template's <c>role</c>, a dependency's <c>pinnedVersion</c> and
/// <c>resolved</c>, what <c>charts/</c> holds being <c>declared</c>); <c>Resolves</c> and <c>Includes</c> as the
/// plugin delivers them, <c>Declares</c>, <c>Overrides</c> and <c>Configures</c> as the definition derives them.
/// The relations are delivered in today's order by <see cref="Relations"/>, because DISL appends the derived
/// relations after the stored ones.
/// </para>
/// <para>
/// <b>Two dependencies of one effective name share an id</b> (finding A10), and each is drawn, with the facts
/// of the first of them, which is the element the id names (<see cref="DislDiagram.ElementById"/>).
/// </para>
/// <para>
/// <b>Two of the four delta kinds are never emitted, and that is the design.</b> Nothing here
/// folds, so no group/ungroup delta exists; nothing here is edited through the model, so an
/// element only ever appears, disappears, or is replaced because the folder changed. A push is
/// <c>Remove</c> then <c>Add</c> for what actually differs.
/// </para>
/// </remarks>
public sealed class HelmElementMapper
{
    /// <summary>The mime-style element kinds this type puts on the wire.</summary>
    public const string ChartType = "helm/chart+chart";
    public const string ValuesType = "helm/chart+values";
    public const string SchemaType = "helm/chart+schema";
    public const string TemplateType = "helm/chart+template";
    public const string PartialType = "helm/chart+partial";
    public const string CrdsType = "helm/chart+crds";
    [Relation]
    public const string DependencyType = "helm/chart+dependency";
    public const string SubchartType = "helm/chart+subchart";
    [UsedImplicitly] // Read only by reflection, into the cross-tier element-types fixture.
    public const string ArchiveType = "helm/chart+archive";
    public const string LockType = "helm/chart+lock";
    [Relation]
    public const string EdgeType = "helm/chart+edge";

    private static readonly string PayloadTypeUrl =
        $"type.googleapis.com/{Wire.HelmElementPayload.Descriptor.FullName}";

    /// <summary>
    /// Every element of the diagram: every node with its box, every edge anchored at its
    /// source. Boxes arrive with authored positions already overlaid - the mapper cannot tell
    /// a dragged box from a computed one, which is the point.
    /// </summary>
    public IReadOnlyList<DiagramElement> Elements(DislDiagram diagram, IReadOnlyDictionary<string, Rect> boxes)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(boxes);

        var relations = Relations(diagram);
        var elements = new List<DiagramElement>(diagram.Nodes.Count + relations.Count);

        foreach (var node in diagram.Nodes)
        {
            elements.Add(NodeElement(diagram, node, boxes.GetValueOrDefault(node.Id)));
        }

        foreach (var relation in relations)
        {
            elements.Add(EdgeElement(relation, boxes.GetValueOrDefault(SourceOf(relation))));
        }

        return elements;
    }

    /// <summary>
    /// The relations of <paramref name="diagram"/> in the order they are delivered, which is today's:
    /// every <c>Overrides</c>, then every <c>Includes</c>, each in model order, then per dependency in model
    /// order its <c>Declares</c>, its <c>Resolves</c> and its <c>Configures</c>.
    /// </summary>
    /// <remarks>
    /// The key is stated because the model's order is not today's: DISL holds the stored relations first,
    /// in reading order, and appends the derived ones in the order of <c>metamodel.relations</c> (DISL 12.5).
    /// A relation of a dependency is placed by the dependency element it is about, its target or, for
    /// <c>Resolves</c>, its source; the sort is stable, so what the key ties keeps the model's order.
    /// </remarks>
    public static IReadOnlyList<DislElement> Relations(DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        var model = diagram;
        var places = new Dictionary<DislElement, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < model.Nodes.Count; index++)
        {
            places[model.Nodes[index]] = index;
        }

        return [.. model.Relations.OrderBy(relation => Key(relation, places))];

        static (int Family, int Dependency, int Kind) Key(DislElement relation, Dictionary<DislElement, int> places) => relation.Type.Name switch
        {
            "Overrides" => (0, 0, 0),
            "Includes" => (1, 0, 0),
            "Declares" => (2, PlaceOf(relation.Target, places), 0),
            "Resolves" => (2, PlaceOf(relation.Source, places), 1),
            "Configures" => (2, PlaceOf(relation.Target, places), 2),
            _ => (3, 0, 0),
        };

        static int PlaceOf(DislElement? node, Dictionary<DislElement, int> places) =>
            node is not null && places.TryGetValue(node, out var place) ? place : int.MaxValue;
    }

    /// <summary>
    /// The elements a viewport admits: every node whose box the rectangle touches, plus one
    /// hop outwards so a connector always has both of its boxes to be drawn between.
    /// </summary>
    /// <remarks>
    /// The one-hop rule is the Ansible module's, kept deliberately rather than reinvented: an
    /// edge with one end in view brings the other end with it, because an edge delivered
    /// without its far box is an edge the client cannot place. An edge whose target never
    /// resolved still draws from its source to nothing, which is how a reader sees that
    /// something is missing rather than seeing nothing at all.
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(
        DislDiagram diagram, IReadOnlyDictionary<string, Rect> boxes, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(diagram);
        ArgumentNullException.ThrowIfNull(boxes);

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in diagram.Nodes.Where(node => boxes.TryGetValue(node.Id, out var box) && viewport.Intersects(box)))
        {
            delivered.Add(node.Id);
        }

        // One hop, exactly.
        var relations = Relations(diagram);
        foreach (var relation in relations)
        {
            var source = SourceOf(relation);
            var target = TargetOf(relation);
            if (delivered.Contains(source) && target.Length > 0)
            {
                delivered.Add(target);
            }
            else if (target.Length > 0 && delivered.Contains(target))
            {
                delivered.Add(source);
            }
        }

        var elements = new List<DiagramElement>();
        foreach (var node in diagram.Nodes.Where(node => delivered.Contains(node.Id)))
        {
            elements.Add(NodeElement(diagram, node, boxes.GetValueOrDefault(node.Id)));
        }

        foreach (var relation in relations)
        {
            var target = TargetOf(relation);
            var sourceDelivered = delivered.Contains(SourceOf(relation));
            var targetDelivered = target.Length == 0 || delivered.Contains(target);
            if (sourceDelivered && targetDelivered)
            {
                elements.Add(EdgeElement(relation, boxes.GetValueOrDefault(SourceOf(relation))));
            }
        }

        return elements;
    }

    /// <summary>The name a node is drawn with: the chart's name, else <c>(chart)</c>; a template's file name; a dependency's effective name; an entry of <c>charts/</c> by its own name; every other node by its path.</summary>
    internal static string NameOf(DislElement node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Type.Name switch
        {
            "Chart" => node.Attributes.GetValueOrDefault("name") as string ?? "(chart)",
            "Template" or "Partial" => FileName(PathOf(node)),
            "Dependency" => Text(node, "alias") is { Length: > 0 } alias ? alias : Text(node, "name"),
            "Subchart" or "Archive" => Text(node, "entryName"),
            _ => PathOf(node),
        };
    }

    /// <summary>The path a node stands for, relative to the chart folder; empty for a dependency, which is a declaration and no file of its own.</summary>
    internal static string PathOf(DislElement node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.IsA("ChartFile") ? Text(node, "path") : string.Empty;
    }

    private static DiagramElement NodeElement(DislDiagram diagram, DislElement node, Rect box)
    {
        // Two nodes may share an id (two dependencies of one effective name): both carry the facts of the one the id names.
        var facts = diagram.ElementById(node.Id) ?? node;
        var payload = new Wire.HelmElementPayload
        {
            Name = NameOf(facts),
            Kind = KindOf(node.Type.Name),
            Width = box.Width,
            Height = box.Height,
        };
        payload.ChartRelativePath.AddRange(Segments(PathOf(node)));

        switch (node.Type.Name)
        {
            case "Chart":
                payload.Chart = ChartFacts(facts);
                Mark(payload, facts.Attributes.GetValueOrDefault("failure"));
                break;

            case "Values":
                payload.Values = new Wire.HelmValuesFacts
                {
                    IsDefault = facts.ValueOf("isDefault") is true,
                    HasGlobal = facts.ValueOf("hasGlobal") is true,
                    KeyCount = Items(facts, "keys").Count,
                };
                Mark(payload, facts.Attributes.GetValueOrDefault("failure"));
                break;

            case "Template" or "Partial":
                payload.Template = new Wire.HelmTemplateFacts
                {
                    Role = RoleOf(facts.ValueOf("role") as string),
                    KindsUndetermined = facts.ValueOf("undetermined") is true,
                };
                payload.Template.Kinds.AddRange(Items(facts, "kinds").OfType<string>());
                payload.Template.Defines.AddRange(Items(facts, "defines").OfType<string>());
                break;

            case "Dependency":
                payload.Dependency = DependencyFacts(facts);
                break;

            case "Subchart" or "Archive":
                payload.Vendored = new Wire.HelmVendoredFacts
                {
                    Sealed = facts.Type.Name == "Archive",
                    Declared = facts.ValueOf("declared") is true,
                    ChartType = Text(facts, "chartType"),
                    ChartVersion = Text(facts, "chartVersion"),
                    TemplateCount = facts.Type.Name == "Subchart" ? Count(facts, "templates") : 0,
                    DeeperCount = facts.Type.Name == "Subchart" ? Count(facts, "deeper") : 0,
                };
                Mark(payload, facts.Attributes.GetValueOrDefault("failure"));
                break;

            case "Crds":
                payload.CrdCount = Count(facts, "files");
                break;

            case "Lock":
                payload.LockEntryCount = Items(facts, "entries").Count;
                Mark(payload, facts.Attributes.GetValueOrDefault("failure"));
                break;
        }

        return new DiagramElement(node.Id, box.X, box.Y, TypeOf(node.Type.Name), PayloadTypeUrl, payload.ToByteArray());
    }

    private static DiagramElement EdgeElement(DislElement relation, Rect anchor)
    {
        var label = relation.ValueOf("label") as string ?? string.Empty;
        var payload = new Wire.HelmElementPayload
        {
            Name = label,
            Kind = Wire.HelmElementKind.Edge,
            Edge = new Wire.HelmEdge
            {
                SourceId = SourceOf(relation),
                TargetId = TargetOf(relation),
                Kind = EdgeKindOf(relation.Type.Name),
                Label = label,
                OpenEnd = relation.Target is null,
            },
        };

        // An edge is positioned at its source; the canvas routes it between the two boxes.
        return new DiagramElement(relation.Id, anchor.X, anchor.Y, EdgeType, PayloadTypeUrl, payload.ToByteArray());
    }

    /// <summary>The id of the node a relation runs from.</summary>
    private static string SourceOf(DislElement relation) => relation.Source?.Id ?? relation.SourceId ?? string.Empty;

    /// <summary>The id of the node a relation lands on; empty for an open end, which has none.</summary>
    private static string TargetOf(DislElement relation) => relation.Target?.Id ?? string.Empty;

    private static Wire.HelmChartFacts ChartFacts(DislElement chart) => new()
    {
        Version = Text(chart, "version"),
        AppVersion = Text(chart, "appVersion"),
        ApiVersion = Text(chart, "apiVersion"),
        ChartType = Text(chart, "chartType"),
        Description = Text(chart, "description"),
        Deprecated = chart.ValueOf("deprecated") is true,
        Legacy = chart.ValueOf("legacy") is true,
    };

    /// <summary>A dependency's facts; the version the lock pins is the definition's <c>pinnedVersion</c>, by the chart name and not the alias, because it records what was fetched.</summary>
    private static Wire.HelmDependencyFacts DependencyFacts(DislElement dependency) => new()
    {
        ChartName = Text(dependency, "name"),
        Alias = Text(dependency, "alias"),
        Constraint = Text(dependency, "constraint"),
        Repository = Text(dependency, "repository"),
        Condition = Text(dependency, "condition"),
        ConditionState = ConditionOf(dependency.ValueOf("conditionState") as string),
        PinnedVersion = Text(dependency, "pinnedVersion"),
        Resolved = dependency.ValueOf("resolved") is true,
    };

    /// <summary>
    /// Marks the payload unreadable with the parser's words and line, when <paramref name="failure"/> is the definition's
    /// <c>YamlFailure</c> of a file that did not parse. It is read as stored: an unset one reads as its type's zero value.
    /// </summary>
    private static void Mark(Wire.HelmElementPayload payload, object? failure)
    {
        if (failure is not CelMap map)
        {
            return;
        }

        payload.Unreadable = true;
        payload.FailureMessage = map.GetValueOrDefault("message") as string ?? string.Empty;
        payload.FailureLine = map.GetValueOrDefault("line") is long line ? (uint)line : 0;
    }

    /// <summary>A string attribute as the model reads it: stored, else its default.</summary>
    private static string Text(DislElement element, string attribute) => element.ValueOf(attribute) as string ?? string.Empty;

    /// <summary>An int attribute as the model reads it.</summary>
    private static int Count(DislElement element, string attribute) => element.ValueOf(attribute) is long value ? (int)value : 0;

    /// <summary>A list attribute as the model reads it.</summary>
    private static IReadOnlyList<object?> Items(DislElement element, string attribute) => element.ValueOf(attribute) as IReadOnlyList<object?> ?? [];

    private static string FileName(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
    }

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Partials get their own wire type so the client styles them apart without reading the payload.</summary>
    private static string TypeOf(string type) => type switch
    {
        "Chart" => ChartType,
        "Values" => ValuesType,
        "Schema" => SchemaType,
        "Partial" => PartialType,
        "Template" => TemplateType,
        "Crds" => CrdsType,
        "Dependency" => DependencyType,
        "Subchart" => SubchartType,
        "Archive" => ArchiveType,
        "Lock" => LockType,
        _ => EdgeType,
    };

    /// <summary>A node's type as the wire's element kind; a partial travels as a template, with its own wire type.</summary>
    private static Wire.HelmElementKind KindOf(string type) => type switch
    {
        "Chart" => Wire.HelmElementKind.Chart,
        "Values" => Wire.HelmElementKind.Values,
        "Schema" => Wire.HelmElementKind.Schema,
        "Template" or "Partial" => Wire.HelmElementKind.Template,
        "Crds" => Wire.HelmElementKind.Crds,
        "Dependency" => Wire.HelmElementKind.Dependency,
        "Subchart" => Wire.HelmElementKind.Subchart,
        "Archive" => Wire.HelmElementKind.Archive,
        "Lock" => Wire.HelmElementKind.Lock,
        _ => Wire.HelmElementKind.Unspecified,
    };

    /// <summary>A template's derived <c>role</c> as the wire's.</summary>
    private static Wire.HelmTemplateRole RoleOf(string? role) => role switch
    {
        "manifest" => Wire.HelmTemplateRole.Manifest,
        "partial" => Wire.HelmTemplateRole.Partial,
        "notes" => Wire.HelmTemplateRole.Notes,
        "test" => Wire.HelmTemplateRole.Test,
        _ => Wire.HelmTemplateRole.Unspecified,
    };

    /// <summary>A dependency's <c>conditionState</c> as the wire's.</summary>
    private static Wire.HelmConditionState ConditionOf(string? state) => state switch
    {
        "none" => Wire.HelmConditionState.ConditionNone,
        "on" => Wire.HelmConditionState.ConditionOn,
        "off" => Wire.HelmConditionState.ConditionOff,
        "missing" => Wire.HelmConditionState.ConditionMissing,
        "unknown" => Wire.HelmConditionState.ConditionUnknown,
        _ => Wire.HelmConditionState.Unspecified,
    };

    /// <summary>A relation type of the definition as the wire's edge kind.</summary>
    private static Wire.HelmEdgeKind EdgeKindOf(string type) => type switch
    {
        "Declares" => Wire.HelmEdgeKind.Declares,
        "Resolves" => Wire.HelmEdgeKind.Resolves,
        "Overrides" => Wire.HelmEdgeKind.Overrides,
        "Configures" => Wire.HelmEdgeKind.Configures,
        "Includes" => Wire.HelmEdgeKind.Includes,
        _ => Wire.HelmEdgeKind.Unspecified,
    };
}
