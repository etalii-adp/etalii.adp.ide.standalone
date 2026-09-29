namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>
/// The kind of <c>strategy</c> a job declares.
/// </summary>
/// <remarks>
/// One enum rather than two, because Azure uses one <c>strategy</c> key for both purposes: a
/// deployment job's strategy decides which lifecycle hooks run (Requirement 4.3), and a plain
/// job's decides how many copies of it run (Requirement 4.6). A job has at most one.
/// </remarks>
public enum PipelineStrategyKind
{
    /// <summary>No <c>strategy</c> declared.</summary>
    None,

    /// <summary>A deployment job that runs its hooks once.</summary>
    RunOnce,

    /// <summary>A deployment job that replaces targets in batches.</summary>
    Rolling,

    /// <summary>A deployment job that shifts traffic in increments.</summary>
    Canary,

    /// <summary>A plain job run once per matrix entry, each with its own variables.</summary>
    Matrix,

    /// <summary>A plain job run as a stated number of identical slices.</summary>
    Parallel,
}
