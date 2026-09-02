namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// An inclusive range of line indices, zero-based - the lines that declare one element or one
/// relation, as the parser recorded them and the writer splices them.
/// </summary>
/// <remarks>
/// Inclusive at both ends because that is how a reader thinks about "the lines that declare this
/// element", and the arithmetic is done once here rather than at every call site.
/// </remarks>
/// <param name="Start">Index of the first line, inclusive.</param>
/// <param name="End">Index of the last line, inclusive.</param>
public readonly record struct LineRange(int Start, int End)
{
    /// <summary>How many lines the range covers.</summary>
    public int Length => End - Start + 1;
}
