using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The cross-check of the folder binding (helm-chart-disl-fbl, Requirement 8.5): every folder of this repository that
/// holds a <c>Chart.yaml</c>, read through the vendored folder binding and the module's plugin by the library's folder
/// body, against today's reader, <see cref="HandWrittenHelmReader"/>, with the ids its graph gave each node and relation.
/// The folder body is given nothing beside the binding, so a directory no file rule can select (gap 3 of the
/// definition's companion, which the module answers with entries of its own) shows here as a divergence. It moved here
/// from <c>EtAlii.Adp.Specification.Fbl.Tests</c> so that no core project references a tool module (code-structure
/// T287); its divergences are still listed in that project's <c>RealFiles/divergences.json</c>.
/// </summary>
public class HelmCrossCheckTests
{
    public static TheoryData<string> ChartFolders()
    {
        var data = new TheoryData<string>();
        foreach (var file in RealFileCorpus.All.Where(file => Path.GetFileName(file) == "Chart.yaml")) data.Add(file[..^"/Chart.yaml".Length]);
        return data;
    }

    [Theory]
    [MemberData(nameof(ChartFolders))]
    public void TheHelmBindingAndItsPluginReadTheIdsTheModuleReads(string folder)
    {
        // Arrange.
        var full = RealFileCorpus.FullPath(folder);
        var binding = RealFileCorpus.Binding("helm-chart.fbl", "chart");

        // Act.
        var reading = FolderBody.ReadOnce(full, binding, new HelmChartFolderPlugin());
        Assert.False(reading.Unreadable, $"{folder} holds a Chart.yaml and is not read as a chart.");
        var fbl = OfBinding(reading);
        var module = OfModule(full);

        // Assert.
        Divergences.Check("cross-check", "helm-chart", folder, ModuleCrossCheck.Difference(fbl, module));
    }

    /// <summary>The binding's side: every element the plugin delivered, by its type, with the id it delivered it with.</summary>
    private static Dictionary<string, SortedSet<string>> OfBinding(FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in model.Elements) ModuleCrossCheck.Add(ids, element.Type, element.Id);
        return ids;
    }

    /// <summary>
    /// The module's side: what today's reader, kept as the module's oracle, reads from the folder, each node by the
    /// definition's type for its kind with the id the module's graph gave it, and each <c>Includes</c> and
    /// <c>Resolves</c> by its id: <c>edge:</c>, its source, its type, its target (empty for an open end) and its label,
    /// <c>|</c> between each.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> OfModule(string folder)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var chart = new HandWrittenHelmReader().Read(folder);
        if (!chart.IsChart) return ids;

        ModuleCrossCheck.Add(ids, "Chart", "chart");
        foreach (var values in chart.Values) ModuleCrossCheck.Add(ids, "Values", $"values:{values.RelativePath}");
        if (chart.Schema is { } schema) ModuleCrossCheck.Add(ids, "Schema", $"schema:{schema.RelativePath}");
        var partials = chart.Templates.Where(template => template.Role == TemplateRole.Partial).ToList();
        foreach (var template in chart.Templates)
        {
            ModuleCrossCheck.Add(ids, template.Role == TemplateRole.Partial ? "Partial" : "Template", $"tpl:{template.RelativePath}");
            foreach (var reference in template.Facts.References)
            {
                var defining = partials.FirstOrDefault(partial => partial.Facts.Defines.Contains(reference, StringComparer.Ordinal));
                ModuleCrossCheck.Add(ids, "Includes", $"edge:tpl:{template.RelativePath}|Includes|{(defining is null ? "" : $"tpl:{defining.RelativePath}")}|{reference}");
            }
        }
        if (chart.Crds is not null) ModuleCrossCheck.Add(ids, "Crds", "crds");
        foreach (var entry in chart.Vendored) ModuleCrossCheck.Add(ids, entry.Sealed ? "Archive" : "Subchart", HandWrittenHelm.VendoredId(entry));
        foreach (var resolved in DependencyResolution.Match(chart.Dependencies, chart.Vendored).Dependencies)
        {
            var dependency = $"dep:{resolved.Dependency.EffectiveName}";
            ModuleCrossCheck.Add(ids, "Dependency", dependency);
            ModuleCrossCheck.Add(ids, "Resolves", resolved.Vendored is { } vendored
                ? $"edge:{dependency}|Resolves|{HandWrittenHelm.VendoredId(vendored)}|"
                : $"edge:{dependency}|Resolves||{resolved.Dependency.EffectiveName}");
        }
        if (chart.Lock is { } chartLock) ModuleCrossCheck.Add(ids, "Lock", $"lock:{chartLock.RelativePath}");
        return ids;
    }
}
