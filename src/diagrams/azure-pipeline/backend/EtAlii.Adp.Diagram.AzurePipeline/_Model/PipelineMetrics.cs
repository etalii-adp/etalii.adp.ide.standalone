namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// The fixed pitch the layout arranges at.
/// </summary>
/// <remarks>
/// Kept as data rather than as constants in the layout so a test can arrange at a pitch it chose
/// and assert on round numbers, and so the canvas and the backend cannot drift apart about how big
/// a stage is. Nothing here is read from the pipeline file: an Azure pipeline has no coordinates
/// and inventing a place to put them is what Requirement 3.3 forbids.
/// </remarks>
/// <param name="StageWidth">How wide a collapsed stage is drawn.</param>
/// <param name="StageHeight">How tall a collapsed stage is drawn.</param>
/// <param name="JobWidth">How wide a job is drawn.</param>
/// <param name="JobHeight">How tall a job is drawn.</param>
/// <param name="HorizontalGap">The space between one layer and the next.</param>
/// <param name="VerticalGap">The space between two elements that can run at the same time.</param>
/// <param name="Padding">The inset between an expanded stage's edge and the jobs inside it.</param>
/// <param name="HeaderHeight">The band at the top of an expanded stage that carries its name.</param>
public sealed record PipelineMetrics(
    double StageWidth = 220,
    double StageHeight = 88,
    double JobWidth = 180,
    double JobHeight = 56,
    double HorizontalGap = 80,
    double VerticalGap = 32,
    double Padding = 24,
    double HeaderHeight = 40)
{
    /// <summary>The pitch the module ships with.</summary>
    public static PipelineMetrics Default { get; } = new();

    /// <summary>How big a collapsed stage is.</summary>
    public PipelineSize Stage => new(StageWidth, StageHeight);

    /// <summary>How big a job is.</summary>
    public PipelineSize Job => new(JobWidth, JobHeight);
}
