namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// A contiguous span of lines, inclusive at both ends: the lines one element of the model
/// occupies in its document.
/// </summary>
/// <remarks>
/// Inclusive rather than start-and-length because every use of it reads as "these lines declare
/// that stage", and an off-by-one in a splice rewrites somebody's pipeline.
/// </remarks>
public readonly record struct PipelineLineRange(int Start, int End)
{
    /// <summary>How many lines the range covers.</summary>
    public int Length => End - Start + 1;

    /// <summary>A range covering the single line at <paramref name="index"/>.</summary>
    public static PipelineLineRange Single(int index) => new(index, index);

    public override string ToString() => Start == End ? $"line {Start}" : $"lines {Start}-{End}";
}
