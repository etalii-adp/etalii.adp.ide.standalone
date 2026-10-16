using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// The findings about dependencies in the tool's order: within a run of findings of one code that are each
/// about a dependency, in the order of the dependencies, which is by effective name (helm-chart-disl-fbl,
/// Requirement 5.2, finding A8).
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for gap 9 of the definition's companion</b> (<c>definition/helm-chart.md</c>). Under
/// <c>constraints.order</c> the runtime puts the findings of one code in document order, by the line of the
/// element a rule was evaluated for and, for a group of duplicates, of its first member (DISL 8.6). The tool
/// reports them in the order of its dependencies, which the reader sorts by effective name: two dependencies
/// whose conditions name a missing path, or two name collisions, come out in the other order whenever the
/// file declares them out of alphabetical order. The <c>same-named-dependencies</c> fixture shows it: the
/// <c>nats</c> collision before the <c>redis</c> one, though <c>redis</c> is declared first. It goes when
/// DISL lets an order follow the model's elements.
/// </para>
/// <para>
/// A finding is placed by the first dependency of the model that holds the id it names, which is where the
/// tool's group of same-named dependencies begins; the sort is stable, so findings about one dependency keep
/// the runtime's order, and every other finding keeps its place.
/// </para>
/// </remarks>
internal static class HelmFindingOrder
{
    /// <summary><paramref name="findings"/> of <paramref name="model"/>, each run of one code about dependencies in dependency order.</summary>
    public static IReadOnlyList<DislFinding> Of(DislDiagram model, IReadOnlyList<DislFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(findings);

        var dependencies = model.NodesOfType("Dependency");
        var places = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < dependencies.Count; index++)
        {
            places.TryAdd(dependencies[index].Id, index);
        }

        var ordered = new List<DislFinding>(findings.Count);
        for (var at = 0; at < findings.Count;)
        {
            if (PlaceOf(findings[at], places) is null)
            {
                ordered.Add(findings[at++]);
                continue;
            }

            var code = findings[at].Code;
            var run = new List<DislFinding>();
            while (at < findings.Count && findings[at].Code == code && PlaceOf(findings[at], places) is not null)
            {
                run.Add(findings[at++]);
            }

            ordered.AddRange(run.OrderBy(finding => PlaceOf(finding, places)));
        }

        return ordered;
    }

    /// <summary>Where among the dependencies the finding's element is; null for a finding about no dependency.</summary>
    private static int? PlaceOf(DislFinding finding, Dictionary<string, int> places) =>
        finding.ElementIds is [var id, ..] && places.TryGetValue(id, out var place) ? place : null;
}
