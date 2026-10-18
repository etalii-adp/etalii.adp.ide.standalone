namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// What an add did: the id of what it created, or why it did not.
/// </summary>
/// <remarks>
/// The id matters as much as the success. An add's inverse is a remove, and a remove needs to know
/// what to remove - so the handler has to hand back what it made rather than merely that it made
/// something.
/// </remarks>
/// <param name="ElementId">What was created; empty on failure.</param>
/// <param name="Error">Why nothing was created; empty on success.</param>
// ReSharper disable once NotAccessedPositionalProperty.Global - Reason: the oracle is the hand-written code moved here as it was (azure-devops-pipeline-disl-fbl task 10); the id was the undo's, which is the host's span restore now, and the record is kept whole so it stays the code it was.
internal sealed record PipelineAddOutcome(string ElementId, string Error)
{
    /// <summary>An add that created <paramref name="elementId"/>.</summary>
    public static PipelineAddOutcome Added(string elementId) => new(elementId, "");

    /// <summary>An add that did nothing, for <paramref name="reason"/>.</summary>
    public static PipelineAddOutcome Failed(string reason) => new("", reason);
}
