using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The Helm chart diagram's parity corpus: every chart folder under the test project's <c>Fixtures/</c>,
/// the module's <c>examples/</c> and the shipped copies under <c>src/examples/diagrams/helm-chart/</c>,
/// each in ordinal order.
/// </summary>
internal static class HelmCorpus
{
    /// <summary>This test project's name.</summary>
    public const string TestProject = "EtAlii.Adp.Diagram.HelmChart.Tests";

    /// <summary>The module's folder, <c>src/diagrams/helm-chart</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "helm-chart");

    /// <summary>The repository's <c>src</c> folder, which every corpus path is relative to.</summary>
    public static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as folder paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Charts()
    {
        return
        [
            .. RepositoryPaths.SortedRelativeToSource(Directory.GetDirectories(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"))),
            .. RepositoryPaths.SortedRelativeToSource(Directory.GetDirectories(Path.Combine(ModuleFolder, "examples"))),
            .. RepositoryPaths.SortedRelativeToSource(Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "helm-chart"))),
        ];
    }
}
