namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// What waits for what, at one level: the stages of a pipeline, or the jobs of one stage.
/// </summary>
/// <param name="NodeIds">The elements, in declared order.</param>
/// <param name="Edges">The dependencies between them.</param>
/// <param name="Cycles">
/// Each cycle found, as the ids going round it. Reported rather than thrown, and the rest of the
/// graph is still usable - a pipeline with a cycle is one somebody needs to see drawn
/// (Requirement 6.6).
/// </param>
public sealed record PipelineGraph(
    IReadOnlyList<string> NodeIds,
    IReadOnlyList<PipelineEdge> Edges,
    IReadOnlyList<IReadOnlyList<string>> Cycles)
{
    /// <summary>A level with nothing in it.</summary>
    public static PipelineGraph Empty { get; } = new([], [], []);

    /// <summary>The edges naming something that does not exist.</summary>
    public IEnumerable<PipelineEdge> BrokenEdges => Edges.Where(edge => edge.IsBroken);

    /// <summary>What <paramref name="nodeId"/> waits for, skipping edges that name nothing.</summary>
    public IEnumerable<string> DependenciesOf(string nodeId) =>
        Edges.Where(edge => !edge.IsBroken && edge.ToId == nodeId).Select(edge => edge.FromId);

    /// <summary>What waits for <paramref name="nodeId"/>.</summary>
    public IEnumerable<string> DependentsOf(string nodeId) =>
        Edges.Where(edge => !edge.IsBroken && edge.FromId == nodeId).Select(edge => edge.ToId);
}
