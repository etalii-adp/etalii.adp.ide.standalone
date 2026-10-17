using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// A chart folder on disk as the module reads it since helm-chart-disl-fbl task 9: through the binding, so a test
/// reads exactly what the host does. <see cref="Reading"/> is the whole reading, the folder body's and the
/// definition's model of it; <see cref="Chart"/> is the reader's own record of what it is handed, for a test about
/// the reading itself.
/// </summary>
internal static class HelmFolder
{
    /// <summary>The fixture chart <paramref name="name"/>, beside the test binary.</summary>
    public static string Fixture(string name) => IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>One reading of <paramref name="folder"/> through the binding and the plugin, leaving nothing open.</summary>
    public static HelmReading Reading(string folder) => HelmBody.Read(folder);

    /// <summary>
    /// The reading of a chart made in memory, as the folder body would read a folder that holds it: what the plugin
    /// delivers for it, and the model built from that; a chart that is not one is an unreadable body.
    /// </summary>
    public static HelmReading Reading(HelmChart chart)
    {
        var read = HelmChartFolderPlugin.Result(chart);
        var folder = chart.IsChart
            ? new FblModel(read.Elements, read.Findings, Unreadable: false)
            : new FblModel([], [new Finding(FindingCodes.Unparseable, FindingSeverity.Error, "The folder is not one this binding recognises, so it is not read.", null)], Unreadable: true);
        return new HelmReading(folder, HelmDefinition.Model(folder));
    }

    /// <summary>
    /// What <see cref="HelmChartReader"/> reads from the files the binding selects in <paramref name="folder"/> and the
    /// directories <see cref="HelmFolderEntries"/> lists, as the folder body hands them to the plugin.
    /// </summary>
    public static HelmChart Chart(string folder) => new HelmChartReader().Read(Request(folder));

    /// <summary>What the folder body hands the plugin for <paramref name="folder"/>: every selected file with its bytes, and the directory entries.</summary>
    public static PluginReadRequest Request(string folder) =>
        new([.. FolderSubject.Files(HelmDefinition.Binding, folder).Select(file => new PluginFile(file.RelativePath, File.ReadAllBytes(file.FullPath)))])
        {
            SupplementaryEntries = HelmFolderEntries.Of(folder),
        };
}
