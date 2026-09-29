namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline;

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
public sealed record PipelineAddOutcome(string ElementId, string Error)
{
    /// <summary>An add that created <paramref name="elementId"/>.</summary>
    public static PipelineAddOutcome Added(string elementId) => new(elementId, "");

    /// <summary>An add that did nothing, for <paramref name="reason"/>.</summary>
    public static PipelineAddOutcome Failed(string reason) => new("", reason);
}
