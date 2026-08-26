namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// A laid-out level: where everything went, and how much room it took.
/// </summary>
/// <param name="Placements">Every element placed, in the order its level declared them.</param>
/// <param name="Size">The bounding box of the whole arrangement, from the origin.</param>
public sealed record PipelineArrangement(IReadOnlyList<PipelinePlacement> Placements, PipelineSize Size)
{
    /// <summary>Nothing to arrange.</summary>
    public static PipelineArrangement Empty { get; } = new([], new PipelineSize(0, 0));

    /// <summary>The placement of one element, or null where it was not in this level.</summary>
    public PipelinePlacement? Of(string id) =>
        Placements.FirstOrDefault(placement => string.Equals(placement.Id, id, StringComparison.Ordinal));
}
