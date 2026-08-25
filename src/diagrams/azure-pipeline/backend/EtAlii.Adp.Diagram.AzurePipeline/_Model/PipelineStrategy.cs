namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// A job's <c>strategy</c> block as declared.
/// </summary>
/// <param name="Kind">Which strategy it is.</param>
/// <param name="Lines">The lines declaring it.</param>
public sealed record PipelineStrategy(PipelineStrategyKind Kind, PipelineLineRange Lines)
{
    /// <summary>The absence of a <c>strategy</c> key.</summary>
    public static PipelineStrategy None { get; } = new(PipelineStrategyKind.None, PipelineLineRange.Single(0));

    /// <summary>Whether this is one of the three deployment strategies.</summary>
    public bool IsDeployment =>
        Kind is PipelineStrategyKind.RunOnce or PipelineStrategyKind.Rolling or PipelineStrategyKind.Canary;
}
