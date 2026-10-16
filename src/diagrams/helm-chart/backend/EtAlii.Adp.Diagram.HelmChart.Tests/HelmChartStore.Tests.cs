using EtAlii.Adp.Documents;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.HelmChart.Tests;

/// <summary>
/// The store (Requirement 1.4): one chart per folder, kept true as the folder changes, through the
/// shared watched-folder contract and the module's own case.
/// </summary>
public sealed class HelmChartStoreTests : WatchedFolderStoreContract<HelmChart>
{
    protected override WatchedFolderStore<HelmChart> CreateStore(TimeSpan settleDelay, TimeProvider time) =>
        new HelmChartStore(new HelmChartReader(), settleDelay, time);

    protected override void Populate(string root) =>
        File.WriteAllText(IoPath.Combine(root, "Chart.yaml"), "apiVersion: v2\nname: watched\nversion: 1.0.0\n");

    /// <summary>The chart gains its default values file.</summary>
    protected override Task ChangeAsync(string root, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(IoPath.Combine(root, "values.yaml"), "replicaCount: 1\n", cancellationToken);

    protected override bool ShowsTheChange(HelmChart snapshot) => snapshot.Values.Count == 1;

    /// <summary>A helm dependency build rewriting charts/ wholesale is the real-world shape of a burst.</summary>
    protected override Task TouchAsync(string root, int index, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(IoPath.Combine(root, $"values-{index}.yaml"), $"layer: {index}\n", cancellationToken);

    [Fact]
    public void ThereIsNoSavePath()
    {
        // Assert.
        // Structural, per Requirement 7.1: the store's public surface has nothing that takes
        // content to write - reflection proves the absence rather than a comment claiming it.
        var writers = typeof(IHelmChartStore).GetInterfaces().Append(typeof(IHelmChartStore))
            .SelectMany(type => type.GetMethods())
            .Where(method => method.Name.Contains("Save", StringComparison.OrdinalIgnoreCase)
                             || method.Name.Contains("Write", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Empty(writers);
    }
}
