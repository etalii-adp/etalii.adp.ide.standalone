namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// Arranges a dependency graph into a picture: a graph in, positions out.
/// </summary>
/// <remarks>
/// <para>
/// An Azure pipeline has no coordinates, so somebody has to choose them, and Requirement 7.1 says
/// how: layers running left to right, where an element's layer is its <b>longest</b> dependency
/// depth. Longest rather than shortest is what makes every arrow point forward - with shortest
/// depth, a stage that waits for both a first and a third stage would sit in column one with an
/// arrow coming backwards into it.
/// </para>
/// <para>
/// Two consequences fall out of that and are worth naming, because they are what the diagram is
/// for: elements in the same column are exactly the ones that can run at the same time, and the
/// number of columns is the length of the pipeline's critical path.
/// </para>
/// <para>
/// Nothing here reads a file, talks to gRPC or knows what a canvas is (Requirement 7.6), and the
/// same graph always produces the same picture (Requirement 7.2) - within a layer, elements sit in
/// the order the file declared them, so renaming a step moves nothing.
/// </para>
/// </remarks>
public static class PipelineLayout
{
    /// <summary>
    /// Arranges one level - the stages of a pipeline, or the jobs of one stage.
    /// </summary>
    /// <param name="graph">What waits for what.</param>
    /// <param name="metrics">The pitch to arrange at.</param>
    /// <param name="size">How big each element is; anything not named gets <paramref name="fallback"/>.</param>
    /// <param name="fallback">The size for an element <paramref name="size"/> says nothing about.</param>
    public static PipelineArrangement Arrange(
        PipelineGraph graph,
        PipelineMetrics metrics,
        IReadOnlyDictionary<string, PipelineSize>? size = null,
        PipelineSize? fallback = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(metrics);

        if (graph.NodeIds.Count == 0)
        {
            return PipelineArrangement.Empty;
        }

        var defaultSize = fallback ?? metrics.Stage;

        var layers = Layers(graph);
        var placements = new List<PipelinePlacement>(graph.NodeIds.Count);

        // A layer is as wide as its widest element, so a column of collapsed stages beside a
        // column of expanded ones does not overlap it.
        var layerWidth = new Dictionary<int, double>();
        foreach (var id in graph.NodeIds)
        {
            var layer = layers[id];
            layerWidth[layer] = Math.Max(layerWidth.GetValueOrDefault(layer), SizeOf(id).Width);
        }

        var layerX = new Dictionary<int, double>();
        var x = 0.0;
        foreach (var layer in layerWidth.Keys.Order())
        {
            layerX[layer] = x;
            x += layerWidth[layer] + metrics.HorizontalGap;
        }

        // Each column is filled top-down in declared order, so where an element ends up depends
        // only on the elements before it in its own column - which is what keeps the picture
        // stable when something elsewhere changes.
        var nextY = new Dictionary<int, double>();
        var nextRow = new Dictionary<int, int>();
        foreach (var id in graph.NodeIds)
        {
            var layer = layers[id];
            var elementSize = SizeOf(id);
            var y = nextY.GetValueOrDefault(layer);
            var row = nextRow.GetValueOrDefault(layer);

            placements.Add(new PipelinePlacement(id, layerX[layer], y, elementSize, layer, row));
            nextY[layer] = y + elementSize.Height + metrics.VerticalGap;
            nextRow[layer] = row + 1;
        }

        // The bounding box stops at the last column's right edge rather than at the gap after it,
        // so an expanded stage sized from this does not carry a stripe of padding on its right.
        var lastLayer = layerX.MaxBy(pair => pair.Value).Key;
        return new PipelineArrangement(
            placements,
            new PipelineSize(
                layerX[lastLayer] + layerWidth[lastLayer],
                placements.Max(placement => placement.Y + placement.Size.Height)));

        PipelineSize SizeOf(string id) => size is not null && size.TryGetValue(id, out var found) ? found : defaultSize;
    }

    /// <summary>
    /// Arranges a whole pipeline: its stages, and the jobs inside whichever of them are expanded.
    /// </summary>
    /// <remarks>
    /// An expanded stage is as big as the jobs inside it, so the two levels have to be laid out
    /// inner-first: the jobs decide the stage's size, and the stage's position then decides where
    /// the jobs actually sit. The same rule applies at both levels, which is the point of
    /// Requirement 7.5 - a reader who has learned to read the stage graph can read the job graph.
    /// </remarks>
    /// <param name="model">The pipeline.</param>
    /// <param name="metrics">The pitch to arrange at.</param>
    /// <param name="expandedIds">Which stages are showing their jobs, and which jobs their steps;
    /// null for none.</param>
    public static PipelineArrangement ArrangePipeline(
        PipelineModel model,
        PipelineMetrics metrics,
        IReadOnlySet<string>? expandedIds = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(metrics);

        var expanded = expandedIds ?? new HashSet<string>(StringComparer.Ordinal);
        var within = new Dictionary<string, PipelineArrangement>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, PipelineSize>(StringComparer.Ordinal);

        foreach (var stage in model.Stages.Where(stage => expanded.Contains(stage.Id)))
        {
            // An open job is as tall as the steps inside it. Steps are a sequence and not a graph
            // (Requirement 3.4), so their room is counted rather than arranged: the job only has to
            // be big enough to hold the column the mapper draws.
            var jobSizes = stage.Jobs
                .Where(job => expanded.Contains(job.Id) && job.Steps.Count > 0)
                .ToDictionary(
                    job => job.Id,
                    job => new PipelineSize(
                        Math.Max(metrics.JobWidth, metrics.JobWidth + (2 * metrics.Padding)),
                        metrics.HeaderHeight + (2 * metrics.Padding) +
                        (job.Steps.Count * metrics.JobHeight) + ((job.Steps.Count - 1) * metrics.VerticalGap)),
                    StringComparer.Ordinal);

            var jobs = Arrange(PipelineGraphBuilder.OfJobs(stage), metrics, jobSizes, metrics.Job);
            within[stage.Id] = jobs;
            sizes[stage.Id] = new PipelineSize(
                Math.Max(metrics.StageWidth, jobs.Size.Width + (2 * metrics.Padding)),
                Math.Max(metrics.StageHeight, jobs.Size.Height + metrics.HeaderHeight + (2 * metrics.Padding)));
        }

        var stages = Arrange(PipelineGraphBuilder.OfStages(model), metrics, sizes, metrics.Stage);

        var placements = new List<PipelinePlacement>(stages.Placements);
        foreach (var stage in stages.Placements.Where(placement => within.ContainsKey(placement.Id)))
        {
            var inside = within[stage.Id];
            placements.AddRange(inside.Placements.Select(job => job with
            {
                X = stage.X + metrics.Padding + job.X,
                Y = stage.Y + metrics.HeaderHeight + metrics.Padding + job.Y,
            }));
        }

        return stages with { Placements = placements };
    }

    /// <summary>
    /// Each element's column: the length of the longest chain of dependencies leading to it.
    /// </summary>
    /// <remarks>
    /// Edges that close a cycle are left out of the calculation, because a cycle has no longest
    /// path and asking for one does not terminate. The graph reports the cycle separately and the
    /// canvas draws it; what this does is refuse to hang over it, so a pipeline with a mistake in
    /// it still produces the picture that shows the mistake.
    /// </remarks>
    private static Dictionary<string, int> Layers(PipelineGraph graph)
    {
        var order = graph.NodeIds
            .Select((id, index) => (id, index))
            .ToDictionary(pair => pair.id, pair => pair.index, StringComparer.Ordinal);

        var forward = graph.NodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in graph.Edges.Where(edge => !edge.IsBroken))
        {
            if (order.ContainsKey(edge.FromId) && forward.TryGetValue(edge.ToId, out var dependencies))
            {
                dependencies.Add(edge.FromId);
            }
        }

        var layers = new Dictionary<string, int>(StringComparer.Ordinal);
        var resolving = new HashSet<string>(StringComparer.Ordinal);

        foreach (var id in graph.NodeIds)
        {
            Depth(id);
        }

        return layers;

        int Depth(string id)
        {
            if (layers.TryGetValue(id, out var known))
            {
                return known;
            }

            // Already on the stack, so following this edge would go round a cycle. Treated as
            // depth zero, which drops the back edge rather than chasing it.
            if (!resolving.Add(id))
            {
                return 0;
            }

            var depth = 0;
            foreach (var dependency in forward[id])
            {
                depth = Math.Max(depth, Depth(dependency) + 1);
            }

            resolving.Remove(id);
            layers[id] = depth;
            return depth;
        }
    }
}
