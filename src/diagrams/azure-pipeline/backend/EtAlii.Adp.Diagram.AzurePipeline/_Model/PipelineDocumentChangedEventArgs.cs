namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>What changed about a pipeline document, for the sessions showing it.</summary>
/// <param name="Path">The document that changed.</param>
/// <param name="Model">What it says now.</param>
public sealed record PipelineDocumentChangedEventArgs(string Path, PipelineModel Model);
