using EtAlii.Adp.Hierarchy;
using Google.Protobuf;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Turns a loaded entry into the core element and delta vocabulary, for whichever of the
/// family's three readings a session shows. Positions come in already merged - computed layout
/// with the authored <c>layout:</c> block overlaid element by element - so this class does no
/// arithmetic of its own.
/// </summary>
/// <remarks>
/// An edge naming an element that does not exist still goes out, carrying the key as written,
/// so the canvas can mark the stub and the validator can report it (Requirement 4.5) - the
/// policy every arranged module here inherits from the Wardley mapper.
/// </remarks>
public sealed class DatabricksElementMapper
{
    /// <summary>The mime-style kinds the canvases switch on.</summary>
    public const string TaskType = "databricks/job+task";

    /// <inheritdoc cref="TaskType" />
    public const string ClusterType = "databricks/job+cluster";

    /// <inheritdoc cref="TaskType" />
    public const string EdgeType = "databricks/job+edge";

    /// <inheritdoc cref="TaskType" />
    public const string BundleType = "databricks/bundle+bundle";

    /// <inheritdoc cref="TaskType" />
    public const string ResourceType = "databricks/bundle+resource";

    /// <inheritdoc cref="TaskType" />
    public const string TargetType = "databricks/bundle+target";

    /// <inheritdoc cref="TaskType" />
    public const string OverrideEdgeType = "databricks/bundle+edge";

    /// <inheritdoc cref="TaskType" />
    public const string PipelineNodeType = "databricks/pipeline+node";

    /// <inheritdoc cref="TaskType" />
    public const string FlowEdgeType = "databricks/pipeline+edge";

    /// <summary>
    /// The sizes the canvas draws a box at, in the module's own units, mirroring
    /// <c>NODE_WIDTH</c>/<c>NODE_HEIGHT</c> and <c>FRAME_WIDTH</c>/<c>FRAME_HEIGHT</c> in
    /// <c>DatabricksCanvas.tsx</c>. Culling needs a box rather than the point an element carries,
    /// and these four numbers are the only thing the backend needs to know about how the canvas
    /// draws - which is why they are copied rather than a new leg of the protocol.
    /// </summary>
    private const double NodeWidth = 200;

    /// <inheritdoc cref="NodeWidth" />
    private const double NodeHeight = 56;

    /// <inheritdoc cref="NodeWidth" />
    private const double FrameWidth = 220;

    /// <inheritdoc cref="NodeWidth" />
    private const double FrameHeight = 120;

    /// <summary>The job reading: the DAG's tasks, stubs, clusters and edges.</summary>
    public IReadOnlyList<DiagramElement> JobElements(
        JobModel job, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(positions);

        var elements = new List<DiagramElement>();
        var declared = job.Tasks
            .Where(task => task.Key.Length > 0)
            .Select(task => task.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var task in job.Tasks.Where(task => task.Key.Length > 0))
        {
            elements.Add(Pack($"task:{task.Key}", At(positions, $"task:{task.Key}"), TaskType,
                new DatabricksTaskPayload
                {
                    TaskKey = task.Key,
                    TaskType = task.Type,
                    Source = task.Source,
                    ClusterKey = task.ClusterKey,
                    RunIf = task.RunIf,
                    Unresolved = false,
                }));

            foreach (var dependency in task.DependsOn)
            {
                elements.Add(Pack($"edge:{dependency.TaskKey}->{task.Key}", default, EdgeType,
                    new DatabricksEdgePayload
                    {
                        Outcome = dependency.Outcome,
                        FromElementId = $"task:{dependency.TaskKey}",
                        ToElementId = $"task:{task.Key}",
                    }));
            }
        }

        // The stubs: keys depends_on names and no task declares, drawn marked-missing so
        // connect and disconnect still work on the healthy parts (Requirement 4.5).
        var stubs = job.Tasks
            .SelectMany(task => task.DependsOn)
            .Select(dependency => dependency.TaskKey)
            .Where(key => !declared.Contains(key))
            .Distinct(StringComparer.Ordinal);
        foreach (var stub in stubs)
        {
            elements.Add(Pack($"task:{stub}", At(positions, $"task:{stub}"), TaskType,
                new DatabricksTaskPayload { TaskKey = stub, TaskType = "other", Unresolved = true }));
        }

        foreach (var cluster in job.Clusters)
        {
            elements.Add(Pack($"cluster:{cluster.Key}", At(positions, $"cluster:{cluster.Key}"), ClusterType,
                new DatabricksPipelineNodePayload
                {
                    Role = "compute",
                    Label = cluster.Key,
                    Badges = { Compacted([cluster.SparkVersion, cluster.NodeType, Workers(cluster)]) },
                }));
        }

        return elements;
    }

    /// <summary>The bundle reading: the bundle, its resources and targets, and the override edges.</summary>
    public IReadOnlyList<DiagramElement> BundleElements(
        BundleModel bundle, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(positions);

        var elements = new List<DiagramElement>
        {
            Pack("bundle", At(positions, "bundle"), BundleType,
                new DatabricksResourcePayload { Kind = "bundle", Key = bundle.Name }),
        };

        foreach (var resource in bundle.Resources)
        {
            var id = $"resource:{resource.Kind}/{resource.Key}";
            elements.Add(Pack(id, At(positions, id), ResourceType,
                new DatabricksResourcePayload { Kind = resource.Kind, Key = resource.Key }));
        }

        foreach (var unknown in bundle.UnknownNodes)
        {
            var id = $"unknown:{unknown.Path}";
            elements.Add(Pack(id, At(positions, id), ResourceType,
                new DatabricksResourcePayload { Kind = unknown.Path, Key = unknown.Key }));
        }

        foreach (var target in bundle.Targets)
        {
            var id = $"target:{target.Name}";
            elements.Add(Pack(id, At(positions, id), TargetType,
                new DatabricksTargetPayload
                {
                    Mode = target.Mode,
                    IsDefault = target.IsDefault,
                    OverrideCount = target.Overrides.Count,
                }));

            // The override edges of Requirement 3.3: target frame to the resource it overrides,
            // dangling ends included so the validator's finding has something to point at.
            foreach (var overridden in target.Overrides)
            {
                elements.Add(Pack($"override:{target.Name}/{overridden.Kind}/{overridden.Key}", default, OverrideEdgeType,
                    new DatabricksEdgePayload
                    {
                        FromElementId = id,
                        ToElementId = $"resource:{overridden.Kind}/{overridden.Key}",
                    }));
            }
        }

        return elements;
    }

    /// <summary>The pipeline reading: sources into the pipeline into its target, satellites beneath.</summary>
    public IReadOnlyList<DiagramElement> PipelineElements(
        PipelineModel pipeline, IReadOnlyDictionary<string, RegistrationPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(positions);

        var elements = new List<DiagramElement>();

        foreach (var library in pipeline.Libraries)
        {
            var id = $"library:{library.Path}";
            elements.Add(Pack(id, At(positions, id), PipelineNodeType,
                new DatabricksPipelineNodePayload { Role = "source", Label = library.Path, Badges = { library.Kind } }));
            elements.Add(Pack($"flow:{library.Path}->pipeline", default, FlowEdgeType,
                new DatabricksEdgePayload { FromElementId = id, ToElementId = "pipeline" }));
        }

        elements.Add(Pack("pipeline", At(positions, "pipeline"), PipelineNodeType,
            new DatabricksPipelineNodePayload
            {
                Role = "pipeline",
                Label = pipeline.Name,
                Badges = { PipelineBadges(pipeline) },
            }));

        var targetLabel = pipeline.Catalog.Length > 0 && pipeline.Schema.Length > 0
            ? $"{pipeline.Catalog}.{pipeline.Schema}"
            : pipeline.Catalog.Length > 0 ? pipeline.Catalog : pipeline.Schema;
        elements.Add(Pack("target", At(positions, "target"), PipelineNodeType,
            new DatabricksPipelineNodePayload { Role = "target", Label = targetLabel }));
        elements.Add(Pack("flow:pipeline->target", default, FlowEdgeType,
            new DatabricksEdgePayload { FromElementId = "pipeline", ToElementId = "target" }));

        elements.Add(Pack("compute", At(positions, "compute"), PipelineNodeType,
            new DatabricksPipelineNodePayload
            {
                Role = "compute",
                Label = pipeline.Serverless ? "serverless" : "cluster",
            }));

        if (pipeline.Notifications.Count > 0)
        {
            elements.Add(Pack("notifications", At(positions, "notifications"), PipelineNodeType,
                new DatabricksPipelineNodePayload
                {
                    Role = "notifications",
                    Label = string.Join(", ", pipeline.Notifications.SelectMany(entry => entry.Recipients)),
                }));
        }

        return elements;
    }

    /// <summary>
    /// The elements a reported viewport can see: every box that intersects it, and - one hop
    /// exactly - the far end of any edge with one end already in view, so a connector is never
    /// drawn to a box that was culled (view-delta-adoption Requirements 1.1, 1.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// An edge carries no position of its own - all three readings pack one at the origin - so it
    /// cannot be tested against the rectangle. Its endpoints decide it, read back from the
    /// payload this class wrote; every edge type in the family carries
    /// <see cref="DatabricksEdgePayload"/>, which is what makes one rule enough for three
    /// readings.
    /// </para>
    /// <para>
    /// The hop is taken against a snapshot of the boxes in view rather than against the growing
    /// set, so the answer does not depend on the order the edges happen to arrive in. A chain of
    /// tasks leading away from the viewport therefore contributes its first link and stops,
    /// which is what "one hop" means; walking the growing set would drag in the whole connected
    /// component for a diagram of any depth.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DiagramElement> Visible(IReadOnlyList<DiagramElement> elements, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(elements);

        var inView = elements
            .Where(element => !IsEdge(element.Type) && Intersects(element, viewport))
            .Select(element => element.Id)
            .ToHashSet(StringComparer.Ordinal);

        var reached = new HashSet<string>(inView, StringComparer.Ordinal);
        foreach (var edge in elements.Where(element => IsEdge(element.Type)))
        {
            var (from, to) = EndsOf(edge);
            if (inView.Contains(from))
            {
                reached.Add(to);
            }
            else if (inView.Contains(to))
            {
                reached.Add(from);
            }
        }

        return elements
            .Where(element => IsEdge(element.Type)
                ? reached.Contains(EndsOf(element).From) && reached.Contains(EndsOf(element).To)
                : reached.Contains(element.Id))
            .ToArray();
    }

    private static (string From, string To) EndsOf(DiagramElement edge)
    {
        var payload = DatabricksEdgePayload.Parser.ParseFrom(edge.Payload.Span);
        return (payload.FromElementId, payload.ToElementId);
    }

    private static bool IsEdge(string type) =>
        type is EdgeType or OverrideEdgeType or FlowEdgeType;

    /// <summary>
    /// Whether a box overlaps the viewport. The drawn sizes are the canvas's own
    /// (<c>NODE_WIDTH</c>/<c>NODE_HEIGHT</c> and the target frame's, in
    /// <c>DatabricksCanvas.tsx</c>): an element's position is its top-left corner, and culling on
    /// the point alone would drop a box the reader can see three quarters of.
    /// </summary>
    private static bool Intersects(DiagramElement element, DiagramViewport viewport)
    {
        var width = element.Type == TargetType ? FrameWidth : NodeWidth;
        var height = element.Type == TargetType ? FrameHeight : NodeHeight;

        return element.X <= viewport.MaxX && element.X + width >= viewport.MinX &&
            element.Y <= viewport.MaxY && element.Y + height >= viewport.MinY;
    }

    private static IEnumerable<string> PipelineBadges(PipelineModel pipeline)
    {
        if (pipeline.Serverless)
        {
            yield return "serverless";
        }

        if (pipeline.Continuous)
        {
            yield return "continuous";
        }

        if (pipeline.Development)
        {
            yield return "development";
        }

        if (pipeline.Channel.Length > 0)
        {
            yield return pipeline.Channel;
        }
    }

    private static string Workers(JobCluster cluster) =>
        cluster.Workers > 0 ? $"{cluster.Workers} workers" : "";

    private static IEnumerable<string> Compacted(IEnumerable<string> badges) =>
        badges.Where(badge => badge.Length > 0);

    private static RegistrationPosition At(
        IReadOnlyDictionary<string, RegistrationPosition> positions, string id) =>
        positions.TryGetValue(id, out var position) ? position : default;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
