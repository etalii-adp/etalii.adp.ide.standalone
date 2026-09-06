using EtAlii.Adp.Diagram;

using Google.Protobuf;

namespace EtAlii.Adp.Diagram.HelmCharts;

/// <summary>
/// Turns a chart, its graph and its boxes into the core element and delta vocabulary - without
/// extending that vocabulary by a single field (Requirement 4.4).
/// </summary>
/// <remarks>
/// <para>
/// <b>The whole diagram is delivered at once - no viewport filtering.</b> A chart is bounded:
/// dozens of nodes even for the prometheus example, so there is nothing to virtualize. (The
/// Ansible module filters by viewport because an Ansible estate is unbounded; a chart is not -
/// the design records the fork.)
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
    public const string DependencyType = "helm/chart+dependency";
    public const string SubchartType = "helm/chart+subchart";
    public const string ArchiveType = "helm/chart+archive";
    public const string LockType = "helm/chart+lock";
    public const string EdgeType = "helm/chart+edge";

    private static readonly string PayloadTypeUrl =
        $"type.googleapis.com/{Wire.HelmElementPayload.Descriptor.FullName}";

    /// <summary>
    /// Every element of the diagram: every node with its box, every edge anchored at its
    /// source. Boxes arrive with authored positions already overlaid - the mapper cannot tell
    /// a dragged box from a computed one, which is the point.
    /// </summary>
    public IReadOnlyList<DiagramElement> Elements(
        HelmChart chart, HelmGraph graph, IReadOnlyDictionary<string, HelmBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(boxes);

        var elements = new List<DiagramElement>(graph.Nodes.Count + graph.Edges.Count);

        foreach (var node in graph.Nodes)
        {
            elements.Add(NodeElement(chart, graph, node, boxes.GetValueOrDefault(node.Id)));
        }

        foreach (var edge in graph.Edges)
        {
            elements.Add(EdgeElement(edge, boxes.GetValueOrDefault(edge.SourceId)));
        }

        return elements;
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
        HelmChart chart, HelmGraph graph, IReadOnlyDictionary<string, HelmBox> boxes, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(boxes);

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes.Where(node => boxes.TryGetValue(node.Id, out var box) && Intersects(box, viewport)))
        {
            delivered.Add(node.Id);
        }

        // One hop, exactly.
        foreach (var edge in graph.Edges)
        {
            if (delivered.Contains(edge.SourceId) && edge.TargetId.Length > 0)
            {
                delivered.Add(edge.TargetId);
            }
            else if (edge.TargetId.Length > 0 && delivered.Contains(edge.TargetId))
            {
                delivered.Add(edge.SourceId);
            }
        }

        var elements = new List<DiagramElement>();
        foreach (var node in graph.Nodes.Where(node => delivered.Contains(node.Id)))
        {
            elements.Add(NodeElement(chart, graph, node, boxes.GetValueOrDefault(node.Id)));
        }

        foreach (var edge in graph.Edges)
        {
            var sourceDelivered = delivered.Contains(edge.SourceId);
            var targetDelivered = edge.TargetId.Length == 0 || delivered.Contains(edge.TargetId);
            if (sourceDelivered && targetDelivered)
            {
                elements.Add(EdgeElement(edge, boxes.GetValueOrDefault(edge.SourceId)));
            }
        }

        return elements;
    }

    private static bool Intersects(HelmBox box, DiagramViewport viewport) =>
        box.X <= viewport.MaxX && box.Right >= viewport.MinX &&
        box.Y <= viewport.MaxY && box.Bottom >= viewport.MinY;

    /// <summary>
    /// What to send a connection whose diagram was <paramref name="previous"/> and is now
    /// <paramref name="current"/>: the ids that went away, then everything that is there now.
    /// Add is an upsert, so re-sending an unchanged element is correct; only ids that genuinely
    /// disappeared need removing first.
    /// </summary>
    public IReadOnlyList<DiagramDelta> Diff(IReadOnlyList<DiagramElement> previous, IReadOnlyList<DiagramElement> current)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);

        var deltas = new List<DiagramDelta>();

        var live = current.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var gone = previous.Select(element => element.Id).Where(id => !live.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        if (current.Count > 0)
        {
            deltas.Add(new DiagramAddDelta(current));
        }

        return deltas;
    }

    private static DiagramElement NodeElement(HelmChart chart, HelmGraph graph, HelmNode node, HelmBox box)
    {
        var payload = new Wire.HelmElementPayload
        {
            Name = node.Name,
            Kind = KindOf(node.Kind),
            Width = box.Width,
            Height = box.Height,
        };
        payload.ChartRelativePath.AddRange(Segments(node.RelativePath));

        switch (node.Kind)
        {
            case HelmNodeKind.Chart:
                payload.Chart = ChartFacts(chart);
                if (chart.MetadataFailure is { } failure)
                {
                    Mark(payload, failure);
                }

                break;

            case HelmNodeKind.Values when ValuesOf(chart, node) is { } values:
                payload.Values = new Wire.HelmValuesFacts
                {
                    IsDefault = values.IsDefault,
                    HasGlobal = values.HasGlobal,
                    KeyCount = values.TopLevelKeys.Count,
                };
                if (values.Failure is { } valuesFailure)
                {
                    Mark(payload, valuesFailure);
                }

                break;

            case HelmNodeKind.Template when TemplateOf(chart, node) is { } template:
                payload.Template = new Wire.HelmTemplateFacts
                {
                    Role = RoleOf(template.Role),
                    KindsUndetermined = template.Role == TemplateRole.Manifest && template.Facts.Kinds.Count == 0,
                };
                payload.Template.Kinds.AddRange(template.Facts.Kinds);
                payload.Template.Defines.AddRange(template.Facts.Defines);
                break;

            case HelmNodeKind.Dependency when ResolvedOf(graph, node) is { } resolved:
                payload.Dependency = DependencyFacts(chart, resolved);
                break;

            case HelmNodeKind.Subchart or HelmNodeKind.Archive when VendoredOf(chart, node) is { } vendored:
                payload.Vendored = new Wire.HelmVendoredFacts
                {
                    Sealed = vendored.Sealed,
                    Declared = graph.Resolution.Dependencies.Any(resolved => ReferenceEquals(resolved.Vendored, vendored)),
                    ChartType = vendored.ChartType,
                    ChartVersion = vendored.ChartVersion ?? string.Empty,
                    TemplateCount = vendored.TemplateCount,
                    DeeperCount = vendored.DeeperCount,
                };
                if (vendored.Failure is { } vendoredFailure)
                {
                    Mark(payload, vendoredFailure);
                }

                break;

            case HelmNodeKind.Crds when chart.Crds is { } crds:
                payload.CrdCount = crds.FileCount;
                break;

            case HelmNodeKind.Lock when chart.Lock is { } chartLock:
                payload.LockEntryCount = chartLock.Entries.Count;
                if (chartLock.Failure is { } lockFailure)
                {
                    Mark(payload, lockFailure);
                }

                break;
        }

        return new DiagramElement(node.Id, box.X, box.Y, TypeOf(node), PayloadTypeUrl, payload.ToByteArray());
    }

    private static DiagramElement EdgeElement(HelmEdge edge, HelmBox anchor)
    {
        var payload = new Wire.HelmElementPayload
        {
            Name = edge.Label,
            Kind = Wire.HelmElementKind.Edge,
            Edge = new Wire.HelmEdge
            {
                SourceId = edge.SourceId,
                TargetId = edge.TargetId,
                Kind = EdgeKindOf(edge.Kind),
                Label = edge.Label,
                OpenEnd = edge.OpenEnd,
            },
        };

        // An edge is positioned at its source; the canvas routes it between the two boxes.
        return new DiagramElement(edge.Id, anchor.X, anchor.Y, EdgeType, PayloadTypeUrl, payload.ToByteArray());
    }

    private static Wire.HelmChartFacts ChartFacts(HelmChart chart) => new()
    {
        Version = chart.Metadata?.Version ?? string.Empty,
        AppVersion = chart.Metadata?.AppVersion ?? string.Empty,
        ApiVersion = chart.Metadata?.ApiVersion ?? string.Empty,
        ChartType = chart.Metadata?.ChartType ?? "application",
        Description = chart.Metadata?.Description ?? string.Empty,
        Deprecated = chart.Metadata?.Deprecated ?? false,
        Legacy = chart.Legacy,
    };

    private static Wire.HelmDependencyFacts DependencyFacts(HelmChart chart, ResolvedDependency resolved)
    {
        var dependency = resolved.Dependency;

        // The lock pins by the chart name, not the alias - it records what was fetched.
        var pinned = chart.Lock?.Entries
            .FirstOrDefault(entry => string.Equals(entry.Name, dependency.Name, StringComparison.Ordinal))
            ?.Version;

        return new Wire.HelmDependencyFacts
        {
            ChartName = dependency.Name,
            Alias = dependency.Alias ?? string.Empty,
            Constraint = dependency.VersionConstraint ?? string.Empty,
            Repository = dependency.Repository ?? string.Empty,
            Condition = dependency.Condition ?? string.Empty,
            ConditionState = ConditionOf(dependency.State),
            PinnedVersion = pinned ?? string.Empty,
            Resolved = resolved.IsResolved,
        };
    }

    private static void Mark(Wire.HelmElementPayload payload, HelmYamlFailure failure)
    {
        payload.Unreadable = true;
        payload.FailureMessage = failure.Message;
        payload.FailureLine = failure.Line;
    }

    private static ValuesFile? ValuesOf(HelmChart chart, HelmNode node) =>
        chart.Values.FirstOrDefault(values => string.Equals(values.RelativePath, node.RelativePath, StringComparison.Ordinal));

    private static TemplateFile? TemplateOf(HelmChart chart, HelmNode node) =>
        chart.Templates.FirstOrDefault(template => string.Equals(template.RelativePath, node.RelativePath, StringComparison.Ordinal));

    private static VendoredEntry? VendoredOf(HelmChart chart, HelmNode node) =>
        chart.Vendored.FirstOrDefault(entry => string.Equals(entry.RelativePath, node.RelativePath, StringComparison.Ordinal));

    private static ResolvedDependency? ResolvedOf(HelmGraph graph, HelmNode node) =>
        graph.Resolution.Dependencies.FirstOrDefault(resolved =>
            string.Equals(HelmGraph.DependencyId(resolved.Dependency), node.Id, StringComparison.Ordinal));

    private static string[] Segments(string relativePath) =>
        relativePath.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Partials get their own wire type so the client styles them apart without reading the payload.</summary>
    private static string TypeOf(HelmNode node) => node.Kind switch
    {
        HelmNodeKind.Chart => ChartType,
        HelmNodeKind.Values => ValuesType,
        HelmNodeKind.Schema => SchemaType,
        HelmNodeKind.Template when node.Name.StartsWith('_') => PartialType,
        HelmNodeKind.Template => TemplateType,
        HelmNodeKind.Crds => CrdsType,
        HelmNodeKind.Dependency => DependencyType,
        HelmNodeKind.Subchart => SubchartType,
        HelmNodeKind.Archive => ArchiveType,
        HelmNodeKind.Lock => LockType,
        _ => EdgeType,
    };

    private static Wire.HelmElementKind KindOf(HelmNodeKind kind) => kind switch
    {
        HelmNodeKind.Chart => Wire.HelmElementKind.Chart,
        HelmNodeKind.Values => Wire.HelmElementKind.Values,
        HelmNodeKind.Schema => Wire.HelmElementKind.Schema,
        HelmNodeKind.Template => Wire.HelmElementKind.Template,
        HelmNodeKind.Crds => Wire.HelmElementKind.Crds,
        HelmNodeKind.Dependency => Wire.HelmElementKind.Dependency,
        HelmNodeKind.Subchart => Wire.HelmElementKind.Subchart,
        HelmNodeKind.Archive => Wire.HelmElementKind.Archive,
        HelmNodeKind.Lock => Wire.HelmElementKind.Lock,
        _ => Wire.HelmElementKind.Unspecified,
    };

    private static Wire.HelmTemplateRole RoleOf(TemplateRole role) => role switch
    {
        TemplateRole.Manifest => Wire.HelmTemplateRole.Manifest,
        TemplateRole.Partial => Wire.HelmTemplateRole.Partial,
        TemplateRole.Notes => Wire.HelmTemplateRole.Notes,
        TemplateRole.Test => Wire.HelmTemplateRole.Test,
        _ => Wire.HelmTemplateRole.Unspecified,
    };

    private static Wire.HelmConditionState ConditionOf(ConditionState state) => state switch
    {
        ConditionState.None => Wire.HelmConditionState.ConditionNone,
        ConditionState.On => Wire.HelmConditionState.ConditionOn,
        ConditionState.Off => Wire.HelmConditionState.ConditionOff,
        ConditionState.Missing => Wire.HelmConditionState.ConditionMissing,
        ConditionState.Unknown => Wire.HelmConditionState.ConditionUnknown,
        _ => Wire.HelmConditionState.Unspecified,
    };

    private static Wire.HelmEdgeKind EdgeKindOf(HelmEdgeKind kind) => kind switch
    {
        HelmEdgeKind.Declares => Wire.HelmEdgeKind.Declares,
        HelmEdgeKind.Resolves => Wire.HelmEdgeKind.Resolves,
        HelmEdgeKind.Overrides => Wire.HelmEdgeKind.Overrides,
        HelmEdgeKind.Configures => Wire.HelmEdgeKind.Configures,
        HelmEdgeKind.Includes => Wire.HelmEdgeKind.Includes,
        _ => Wire.HelmEdgeKind.Unspecified,
    };
}
