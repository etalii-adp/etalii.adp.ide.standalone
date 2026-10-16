namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One <c>Resolves</c> or <c>Includes</c> relation as the plugin delivers it: its id, its type, the node it runs
/// from, the node it lands on (null for an open end) and its label.
/// </summary>
internal sealed record HelmOpenEndRelation(string Id, string Type, string SourceId, string? TargetId, string Label);

/// <summary>
/// The two relations of a chart that may have an open end, <c>Includes</c> and <c>Resolves</c>, resolved against
/// what is actually in the folder, with today's ids (helm-chart-disl-fbl, Requirement 3.4, Q2).
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for gap 7 of the definition's companion</b> (<c>definition/helm-chart.md</c>; finding A5). DISL
/// derives a relation only between two elements, so a relation without a target cannot be derived; these two
/// are made here and delivered as stored relations whose target end is <c>optional</c>. It is what is left of
/// the graph the module once made, and goes when DISL lets a derived relation have no target.
/// </para>
/// <para>
/// <b>An open end is a state, never an exception</b>: an unvendored dependency and an include no local partial
/// defines are both delivered with their identity intact, so the relation stays the same relation when what it
/// names finally appears. Every include comes first, in template order, then every dependency's
/// <c>Resolves</c>, in dependency order.
/// </para>
/// <para>
/// <b>One relation per id</b> (the ruling of 2026-10-16, Requirement 1.4): two dependencies of one effective
/// name, both unvendored or both resolved to one entry, make one <c>Resolves</c> twice, with one id and the same
/// content, and the second is not delivered.
/// </para>
/// </remarks>
internal static class HelmOpenEndRelations
{
    public static IReadOnlyList<HelmOpenEndRelation> Of(HelmChart chart, DependencyResolutionResult resolution)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(resolution);

        var relations = new List<HelmOpenEndRelation>();
        var partials = chart.Templates.Where(template => template.Role == TemplateRole.Partial).ToArray();
        foreach (var template in chart.Templates)
        {
            foreach (var reference in template.Facts.References)
            {
                var defining = partials.FirstOrDefault(partial => partial.Facts.Defines.Contains(reference, StringComparer.Ordinal));
                relations.Add(Relation("Includes", HelmChartFolderPlugin.TemplateId(template), defining is null ? null : HelmChartFolderPlugin.TemplateId(defining), reference));
            }
        }

        foreach (var resolved in resolution.Dependencies)
        {
            var dependency = resolved.Dependency;
            relations.Add(resolved.Vendored is { } vendored
                ? Relation("Resolves", HelmChartFolderPlugin.DependencyId(dependency), HelmChartFolderPlugin.VendoredId(vendored), string.Empty)
                : Relation("Resolves", HelmChartFolderPlugin.DependencyId(dependency), null, dependency.EffectiveName));
        }

        var delivered = new HashSet<string>(StringComparer.Ordinal);
        return [.. relations.Where(relation => delivered.Add(relation.Id))];
    }

    /// <summary>A relation with today's id: <c>edge:</c>, its source, its type, its target (empty for an open end) and its label, <c>|</c> between each.</summary>
    private static HelmOpenEndRelation Relation(string type, string sourceId, string? targetId, string label) =>
        new($"edge:{sourceId}|{type}|{targetId ?? string.Empty}|{label}", type, sourceId, targetId, label);
}
