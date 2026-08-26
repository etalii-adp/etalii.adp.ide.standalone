using System.Text.RegularExpressions;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Derives what waits for what, applying the schema's rules exactly.
/// </summary>
/// <remarks>
/// <para>
/// The rules are the reason this diagram is worth having, and they are opposite at the two levels
/// it works on. <b>Stages</b> with no <c>dependsOn</c> run one after another, so the ordering is
/// there in the file without being written down. <b>Jobs</b> with no <c>dependsOn</c> run all at
/// once. The same two lines of YAML therefore mean different things depending on which key they
/// sit under, which is exactly the sort of thing a picture is better at than a file.
/// </para>
/// <para>
/// Nothing here throws. A name that matches nothing keeps its edge and marks it broken; a cycle is
/// recorded and the rest of the graph is still built. Both are mistakes a reader opened the diagram
/// to find, and a component that refused to produce a graph would hide the very thing it was asked
/// about.
/// </para>
/// </remarks>
public static partial class PipelineGraphBuilder
{
    /// <summary>The stage-level graph of a pipeline.</summary>
    public static PipelineGraph OfStages(PipelineModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return Build(
            model.Stages.Select(stage => new PipelineGraphNode(
                stage.Id,
                stage.Name,
                stage.DependsOn,
                stage.DependsOnDeclared,
                stage.Execution.Condition)),
            sequentialByDefault: true);
    }

    /// <summary>The job-level graph within one stage.</summary>
    public static PipelineGraph OfJobs(PipelineStage stage)
    {
        ArgumentNullException.ThrowIfNull(stage);
        return Build(
            stage.Jobs.Select(job => new PipelineGraphNode(
                job.Id,
                job.Name,
                job.DependsOn,
                job.DependsOnDeclared,
                job.Execution.Condition)),
            sequentialByDefault: false);
    }

    private static PipelineGraph Build(IEnumerable<PipelineGraphNode> source, bool sequentialByDefault)
    {
        var nodes = source.ToList();
        if (nodes.Count == 0)
        {
            return PipelineGraph.Empty;
        }

        // Azure matches dependsOn against the element's name, which is not the id: a job's id
        // carries its stage so that two stages may each have a job called Test.
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes.Where(node => node.Name.Length > 0))
        {
            byName.TryAdd(node.Name, node.Id);
        }

        var edges = new List<PipelineEdge>();
        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var condition = Classify(node.Condition);

            if (node.DependsOn.Count > 0)
            {
                edges.AddRange(node.DependsOn.Select(name => new PipelineEdge(
                    byName.TryGetValue(name, out var fromId) ? fromId : "",
                    node.Id,
                    name,
                    condition,
                    node.Condition,
                    IsImplicit: false,
                    IsBroken: !byName.ContainsKey(name))));
                continue;
            }

            // No names. Either the file said "wait for nothing" with `dependsOn: []`, or it said
            // nothing at all - and only in the second case does the stage default apply.
            if (node.DependsOnDeclared || !sequentialByDefault || index == 0)
            {
                continue;
            }

            var previous = nodes[index - 1];
            edges.Add(new PipelineEdge(
                previous.Id,
                node.Id,
                previous.Name,
                condition,
                node.Condition,
                IsImplicit: true,
                IsBroken: false));
        }

        return new PipelineGraph(
            nodes.Select(node => node.Id).ToList(),
            edges,
            FindCycles(nodes.Select(node => node.Id).ToList(), edges));
    }

    /// <summary>
    /// Which outcome a condition waits for.
    /// </summary>
    /// <remarks>
    /// Only the forms Azure documents are recognised, and only when the condition is nothing but
    /// that form. <c>and(succeeded(), eq(...))</c> is deliberately <see cref="PipelineEdgeCondition.Custom"/>:
    /// it does wait for success, but it waits for something else as well, and an arrow drawn as a
    /// plain success edge would be telling the reader the second half does not exist.
    /// </remarks>
    public static PipelineEdgeCondition Classify(string condition)
    {
        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        var trimmed = (condition ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return PipelineEdgeCondition.OnSuccess;
        }

        var match = WellKnownCondition().Match(trimmed);
        return match.Success
            ? match.Groups["name"].Value.ToLowerInvariant() switch
            {
                "succeeded" => PipelineEdgeCondition.OnSuccess,
                "failed" => PipelineEdgeCondition.OnFailure,
                "always" => PipelineEdgeCondition.Always,
                "succeededorfailed" => PipelineEdgeCondition.OnSuccessOrFailure,
                _ => PipelineEdgeCondition.Custom,
            }
            : PipelineEdgeCondition.Custom;
    }

    /// <summary>
    /// One of the documented status functions, alone, optionally naming the elements it asks about.
    /// </summary>
    [GeneratedRegex(
        @"^(?<name>succeeded|failed|always|succeededOrFailed)\(\s*(?<args>'[^']*'(\s*,\s*'[^']*')*)?\s*\)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WellKnownCondition();

    /// <summary>
    /// Every cycle in the graph, each as the ids going round it.
    /// </summary>
    /// <remarks>
    /// A depth-first walk keeping the current path, so the cycle can be reported as the elements
    /// involved rather than as the bare fact that there is one - "Build waits for Test waits for
    /// Build" is actionable and "this pipeline has a cycle" is not.
    /// </remarks>
    private static List<IReadOnlyList<string>> FindCycles(List<string> nodeIds, List<PipelineEdge> edges)
    {
        var outgoing = nodeIds.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var edge in edges.Where(edge => !edge.IsBroken))
        {
            if (outgoing.TryGetValue(edge.FromId, out var targets))
            {
                targets.Add(edge.ToId);
            }
        }

        var cycles = new List<IReadOnlyList<string>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var onPath = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        var reported = new HashSet<string>(StringComparer.Ordinal);

        void Walk(string id)
        {
            seen.Add(id);
            onPath.Add(id);
            path.Add(id);

            foreach (var next in outgoing[id])
            {
                if (onPath.Contains(next))
                {
                    var cycle = path[path.IndexOf(next)..];
                    // The same loop is reachable from every node on it, so it is keyed by its
                    // members rather than reported once per way in.
                    if (reported.Add(string.Join(' ', cycle.Order(StringComparer.Ordinal))))
                    {
                        cycles.Add(cycle);
                    }
                }
                else if (!seen.Contains(next))
                {
                    Walk(next);
                }
            }

            path.RemoveAt(path.Count - 1);
            onPath.Remove(id);
        }

        foreach (var id in nodeIds.Where(id => !seen.Contains(id)))
        {
            Walk(id);
        }

        return cycles;
    }
}
