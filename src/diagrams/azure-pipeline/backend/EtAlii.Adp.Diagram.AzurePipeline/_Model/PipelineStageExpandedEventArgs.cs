namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>One connection opened or closed one stage.</summary>
/// <param name="WatchId">The connection that did it; no other is affected.</param>
/// <param name="BodyPath">The pipeline it happened on.</param>
/// <param name="StageId">The stage.</param>
/// <param name="Expanded">Whether it is now showing its jobs.</param>
public sealed record PipelineStageExpandedEventArgs(
    ShortGuid WatchId,
    string BodyPath,
    string StageId,
    bool Expanded);
