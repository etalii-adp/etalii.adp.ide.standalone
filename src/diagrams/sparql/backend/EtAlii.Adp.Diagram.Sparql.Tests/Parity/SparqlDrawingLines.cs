namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// A drawing as lines to compare: the header, the two numbers, and every drawn record with all its fields, in the
/// order it is drawn, so that a comparison names the first element that differs and the field it differs in.
/// </summary>
internal static class SparqlDrawingLines
{
    public static IReadOnlyList<string> Of(SparqlProjectionResult drawing) =>
    [
        $"header {drawing.HeaderForm} | {string.Join(" | ", drawing.HeaderRows)}",
        $"shown {drawing.Shown} of {drawing.Total}",
        .. drawing.Regions.Select(region => region.ToString()),
        .. drawing.Nodes.Select(node => node.ToString()),
        .. drawing.Edges.Select(edge => edge.ToString()),
        .. drawing.Annotations.Select(annotation => annotation.ToString()),
    ];
}
