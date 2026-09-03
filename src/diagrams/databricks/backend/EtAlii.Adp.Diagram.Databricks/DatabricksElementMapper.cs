using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;
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
    /// The difference between two renderings, as adds and removes - an edit is an add carrying
    /// the element in its new state.
    /// </summary>
    public IReadOnlyList<DiagramDelta> Diff(
        IReadOnlyList<DiagramElement> before,
        IReadOnlyList<DiagramElement> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var previous = before.ToDictionary(element => element.Id, StringComparer.Ordinal);
        var deltas = new List<DiagramDelta>();

        var changed = after
            .Where(element => !previous.TryGetValue(element.Id, out var was) || !Same(was, element))
            .ToArray();
        if (changed.Length > 0)
        {
            deltas.Add(new DiagramAddDelta(changed));
        }

        var current = after.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
        var gone = before.Select(element => element.Id).Where(id => !current.Contains(id)).ToArray();
        if (gone.Length > 0)
        {
            deltas.Add(new DiagramRemoveDelta(gone));
        }

        return deltas;
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

    /// <summary>
    /// Whether two renderings of one element say the same thing. Not the record's own equality:
    /// <see cref="ReadOnlyMemory{T}"/> compares its reference rather than its bytes.
    /// </summary>
    private static bool Same(DiagramElement left, DiagramElement right) =>
        left.X.Equals(right.X)
        && left.Y.Equals(right.Y)
        && left.Type == right.Type
        && left.Payload.Span.SequenceEqual(right.Payload.Span);

    private static RegistrationPosition At(
        IReadOnlyDictionary<string, RegistrationPosition> positions, string id) =>
        positions.TryGetValue(id, out var position) ? position : default;

    private static DiagramElement Pack(string id, RegistrationPosition at, string type, IMessage payload) =>
        new(id, at.X, at.Y, type, $"type.googleapis.com/{payload.Descriptor.FullName}", payload.ToByteArray());
}
