namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The reader asked for a folder on disk, as it was before helm-chart-disl-fbl task 9 made it read only what it is
/// handed: it reads what the binding selects there (<see cref="HelmFolder.Chart"/>). <c>ZeroWrites.Tests.cs</c> asks
/// for a folder this way and is never edited (tasks.md, task 1), so the shape stays in the test project for it.
/// </summary>
internal static class HelmChartReaderFolders
{
    public static HelmChart Read(this HelmChartReader reader, string folder) => reader.Read(HelmFolder.Request(folder));
}
