namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

/// <summary>One connection opened or closed one stage.</summary>
/// <param name="WatchId">The connection that did it; no other is affected.</param>
/// <param name="BodyPath">The pipeline it happened on.</param>
/// <param name="ElementId">The stage.</param>
/// <param name="Expanded">Whether it is now showing what it contains.</param>
public sealed record PipelineElementExpandedEventArgs(
    ShortGuid WatchId,
    string BodyPath,
    string ElementId,
    bool Expanded);
