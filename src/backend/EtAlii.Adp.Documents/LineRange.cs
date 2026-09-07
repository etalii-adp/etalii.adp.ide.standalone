using EtAlii.Adp.Common;
namespace EtAlii.Adp.Documents;

/// <summary>
/// An inclusive range of line indices, zero-based - the lines that declare one construct, as a
/// parser recorded them and a writer splices them.
/// </summary>
/// <remarks>
/// <para>
/// Inclusive at both ends because that is how a reader thinks about "the lines that declare
/// this element", and the arithmetic is done once here rather than at every call site.
/// </para>
/// <para>
/// This lived four times over - in databricks, dependency-graph, rdf and timeline - as four
/// identical declarations differing only in namespace and in which noun the summary named.
/// Four consumers of one value type is past arguing about (file-io-centralization Requirement
/// 4.1), so it moved here rather than staying a shape each new line-splicing module copied.
/// </para>
/// </remarks>
/// <param name="Start">Index of the first line, inclusive.</param>
/// <param name="End">Index of the last line, inclusive.</param>
public readonly record struct LineRange(int Start, int End)
{
    /// <summary>How many lines the range covers.</summary>
    public int Length => End - Start + 1;
}
