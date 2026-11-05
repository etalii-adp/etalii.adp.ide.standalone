using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// The element-id vocabulary, parsed in one place: what a selected id names, and how to describe
/// it to a person. The ids themselves are the bundled definition's (<see cref="SparqlDefinition"/>); this is the
/// only code that takes them apart.
/// </summary>
public static class SparqlSelection
{
    /// <summary>Whether a path could be a query file at all - cheap enough to ask before loading anything.</summary>
    public static bool CouldBeQueryFile(string? path) =>
        path is { Length: > 0 } && path.EndsWith(".rq", StringComparison.OrdinalIgnoreCase);

    /// <summary>The projection of a loaded entry, or null when the file does not parse.</summary>
    private static SparqlProjectionResult? ProjectionOf(DocumentEntry<string, SparqlDocument> entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.IsUsable ? entry.Model.Drawing : null;
    }

    /// <summary>The node an id names, or null.</summary>
    public static SparqlNode? NodeOf(SparqlProjectionResult projection, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return elementId is { Length: > 0 }
            ? projection.Nodes.FirstOrDefault(node => node.Id == elementId)
            : null;
    }

    /// <summary>The region an id names, or null.</summary>
    public static SparqlRegion? RegionOf(SparqlProjectionResult projection, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return elementId is { Length: > 0 }
            ? projection.Regions.FirstOrDefault(region => region.Id == elementId)
            : null;
    }

    /// <summary>The edge an id names, or null.</summary>
    public static SparqlEdge? EdgeOf(SparqlProjectionResult projection, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return elementId is { Length: > 0 }
            ? projection.Edges.FirstOrDefault(edge => edge.Id == elementId)
            : null;
    }

    /// <summary>The annotation an id names, or null.</summary>
    public static SparqlAnnotation? AnnotationOf(SparqlProjectionResult projection, string? elementId)
    {
        ArgumentNullException.ThrowIfNull(projection);
        return elementId is { Length: > 0 }
            ? projection.Annotations.FirstOrDefault(annotation => annotation.Id == elementId)
            : null;
    }

    /// <summary>How this element reads in a selection path, or null when the id is not in the file.</summary>
    public static string? Describe(DocumentEntry<string, SparqlDocument> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (elementId == SparqlElementMapper.HeaderId)
        {
            // The header is always there, even for a file that does not parse.
            return entry.IsUsable ? $"{entry.Model.Drawing.HeaderForm} query" : "Query";
        }

        if (ProjectionOf(entry) is not { } projection)
        {
            return null;
        }

        if (NodeOf(projection, elementId) is { } node)
        {
            return node.Kind switch
            {
                SparqlNodeKind.SubSelect => "Subquery",
                SparqlNodeKind.Anonymous => "Anonymous variable",
                _ => node.Display,
            };
        }

        if (RegionOf(projection, elementId) is { } region)
        {
            return region.Label.Length > 0 ? region.Label : $"{region.Kind} group";
        }

        if (EdgeOf(projection, elementId) is { } edge)
        {
            return edge.Label;
        }

        if (AnnotationOf(projection, elementId) is { } annotation)
        {
            return annotation.Text;
        }

        if (elementId == SparqlElementMapper.TruncationId && projection.Truncated)
        {
            return $"Showing {projection.Shown} of {projection.Total}";
        }

        return null;
    }
}
