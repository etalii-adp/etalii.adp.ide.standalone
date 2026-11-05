using System.Text.Json;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// The link checks as a chain: per connection, the first of dangling, self, unknown relation and
/// inadmissible pair it fails, and no later one, nor the loop of one a self-link is to <c>std.acyclic</c>;
/// and the cardinality of connections whose shared end names no element.
/// </summary>
/// <remarks>
/// <para>
/// <b>Stands in for Q4's default, held to today's findings</b> (finding A11 of
/// functional-decomposition-graph-disl-fbl): DISL's built-ins are independent, so <c>std.references</c>
/// reports each end that names nothing and <c>std.endpoints</c> a loop and a pair both, where the tool
/// reports one finding per connection. Deleted, part by part, as the user rules on Q4's items one by one.
/// </para>
/// <para>
/// <b>A self-link is no ownership loop</b>: <c>diagram.cycles</c> counts a self-loop as a cycle of one, so
/// <c>std.acyclic</c> reports an ownership self-link a second time, as "Ownership loops: a -> a.". It is
/// <c>fdg.self-link</c>'s alone, as the chain keeps every connection's first finding alone. Every loop of
/// two members or more is <c>std.acyclic</c>'s as it reports it (ruled by the user, 2026-10-22).
/// </para>
/// <para>
/// <b>Stands in for gap 11</b> of the companion as well: CEL cannot read the id an unresolved end was
/// written with, so <c>std.multiplicity</c> counts such an end for nothing, where the tool counts
/// connections by the text of their ends and reports two connections to an id no element has as a
/// second parent. This words that finding from <see cref="DislElement.TargetId"/> and
/// <see cref="DislElement.SourceId"/>, against the <c>max</c> each relation type declares.
/// </para>
/// </remarks>
internal static class FdgLinkChain
{
    private const string SecondParent = "fdg.second-parent";
    private const string SecondShows = "fdg.second-shows";

    /// <summary><paramref name="findings"/> with each connection's later link findings left out, and the unresolved ends' cardinality added.</summary>
    public static IReadOnlyList<DislFinding> Apply(IReadOnlyList<DislFinding> findings, DislDiagram diagram)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(diagram);

        var first = new Dictionary<(int?, string), (int Rank, DislFinding Finding)>();
        foreach (var finding in findings)
        {
            if (Rank(finding) is not { } rank) continue;
            var key = (finding.Line, finding.ElementIds.FirstOrDefault() ?? "");
            if (!first.TryGetValue(key, out var kept) || rank < kept.Rank) first[key] = (rank, finding);
        }

        var keep = first.Values.Select(entry => entry.Finding).ToHashSet(ReferenceEqualityComparer.Instance);
        return [.. findings.Where(finding => !IsSelfLoop(finding) && (Rank(finding) is null || keep.Contains(finding))), .. Unresolved(diagram)];
    }

    /// <summary>Whether <paramref name="finding"/> is <c>std.acyclic</c>'s cycle of one, a self-link's.</summary>
    private static bool IsSelfLoop(DislFinding finding) => finding is { ConstraintId: "std.acyclic", ElementIds.Count: 1 };

    /// <summary>A link finding's place in the chain: dangling, self, unknown relation, inadmissible pair; null for any other finding.</summary>
    private static int? Rank(DislFinding finding) => finding.ConstraintId switch
    {
        "std.references" => 0,
        "std.endpoints" when finding.Code == "fdg.self-link" => 1,
        "unknownRelation" => 2,
        "std.endpoints" => 3,
        _ => null,
    };

    /// <summary>The cardinality findings of connections whose shared end names no element, by relation type and end, as the tool counts them.</summary>
    private static IEnumerable<DislFinding> Unresolved(DislDiagram diagram)
    {
        foreach (var type in diagram.Specification.Metamodel.Relations.Values)
        {
            var relations = diagram.Relations.Where(relation => relation.Type == type && !relation.IsDerived).ToList();
            if (Max(type, "target") is { } intoTarget)
            {
                foreach (var group in relations.Where(relation => relation.Target is null && !string.IsNullOrEmpty(relation.TargetId))
                             .GroupBy(relation => relation.TargetId!, StringComparer.Ordinal)
                             .Where(group => group.Count() > intoTarget))
                {
                    yield return Finding(SecondParent, $"`{group.Key}` has {group.Count()} `{FdgReading.DocumentTypeOf(type.Name)}` parents; it may have {intoTarget}.", group.Key, [.. group]);
                }
            }

            if (Max(type, "source") is { } fromSource)
            {
                foreach (var group in relations.Where(relation => relation.Source is null && !string.IsNullOrEmpty(relation.SourceId))
                             .GroupBy(relation => relation.SourceId!, StringComparer.Ordinal)
                             .Where(group => group.Count() > fromSource))
                {
                    yield return Finding(SecondShows, $"`{group.Key}` has {group.Count()} `{FdgReading.DocumentTypeOf(type.Name)}` links; it may have {fromSource}.", group.Key, [.. group]);
                }
            }
        }
    }

    /// <summary>The <c>max</c> the relation type's own declaration states on <paramref name="side"/>; null when it states none.</summary>
    private static long? Max(DislType type, string side) =>
        type.Json.ValueKind == JsonValueKind.Object
        && type.Json.TryGetProperty(side, out var end)
        && end.ValueKind == JsonValueKind.Object
        && end.TryGetProperty("max", out var max)
        && max.TryGetInt64(out var value)
            ? value
            : null;

    private static DislFinding Finding(string code, string message, string end, IReadOnlyList<DislElement> relations) =>
        new(code, "std.multiplicity", "error", message, [end, .. relations.Select(FdgDefinition.WrittenId)], relations[^1].Line);
}
