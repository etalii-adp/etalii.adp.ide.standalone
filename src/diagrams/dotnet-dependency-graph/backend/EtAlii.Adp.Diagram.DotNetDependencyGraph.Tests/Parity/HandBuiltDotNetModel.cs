using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests.Parity;

/// <summary>
/// A graph as the definition's model, built by hand: a node per project and package, a relation per reference. It
/// is the oracle the builder parity test holds the shared builder's model to, moved here from
/// <c>DotNetDefinition.Build</c> when the rows began to read the builder's model (dotnet-dependency-graph-disl-fbl,
/// task 6), so that no product code builds a DISL model by hand.
/// </summary>
internal static class HandBuiltDotNetModel
{
    /// <summary><paramref name="graph"/> as the definition's model, element for element and with the graph's own ids.</summary>
    public static DislDiagram Build(DependencyGraphModel graph)
    {
        var diagram = new DislDiagram(DotNetDefinition.Specification);
        var nodes = new Dictionary<string, DislElement>(StringComparer.Ordinal);
        foreach (var project in graph.Projects)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = project.Name,
                ["path"] = project.RelativePath,
                ["targetFrameworks"] = project.TargetFrameworks.Cast<object?>().ToList(),
            };
            if (project.DotNetVersion is { } version) attributes["dotNetVersion"] = version;
            nodes.TryAdd(project.Id, diagram.AddNode("Project", project.Id, attributes));
        }

        foreach (var package in graph.Packages)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = package.PackageId,
                ["versions"] = package.Versions.Cast<object?>().ToList(),
                ["hasVersionConflict"] = package.HasVersionConflict,
                ["dependentProjectCount"] = (long)package.DependentProjectCount,
                ["isAmbient"] = package.IsAmbient,
            };
            if (package.Description is { } description) attributes["description"] = description;
            nodes.TryAdd(package.Id, diagram.AddNode("Package", package.Id, attributes));
        }

        // A reference whose end the graph does not hold - a package id spelled in another casing - is
        // kept with that end missing, as the definition's model can have it; the canvas draws neither.
        foreach (var edge in graph.Edges)
        {
            diagram.AddRelation(
                edge.Kind == DependsOnKind.Project ? "ProjectReference" : "PackageReference",
                edge.Id,
                nodes.GetValueOrDefault(edge.FromElementId),
                nodes.GetValueOrDefault(edge.ToElementId));
        }

        return diagram;
    }
}
