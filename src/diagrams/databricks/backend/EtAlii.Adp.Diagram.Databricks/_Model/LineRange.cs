namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// An inclusive range of line indices, zero-based - the lines that declare one construct, as a
/// parser recorded them and a writer splices them.
/// </summary>
/// <remarks>
/// Inclusive at both ends because that is how a reader thinks about "the lines that declare this
/// task", and the arithmetic is done once here rather than at every call site.
/// </remarks>
/// <param name="Start">Index of the first line, inclusive.</param>
/// <param name="End">Index of the last line, inclusive.</param>
public readonly record struct LineRange(int Start, int End)
{
    /// <summary>How many lines the range covers.</summary>
    public int Length => End - Start + 1;
}
