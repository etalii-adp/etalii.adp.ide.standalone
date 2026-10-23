using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The causal loop diagram's parity corpus: the test project's <c>Fixtures/*.cld</c>, the module's
/// <c>examples/*/*.cld</c> and the shipped copies under <c>src/examples/diagrams/causal-loop-diagram/</c>.
/// </summary>
internal static class CausalLoopCorpus
{
    /// <summary>This test project's name.</summary>
    public const string TestProject = "EtAlii.Adp.Diagram.CausalLoopDiagram.Tests";

    /// <summary>The module's folder, <c>src/diagrams/causal-loop-diagram</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "causal-loop-diagram");

    /// <summary>The repository's <c>src</c> folder, which every corpus path is relative to.</summary>
    public static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Paths()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"), "*.cld");
        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples")).SelectMany(folder => Directory.GetFiles(folder, "*.cld"));
        var shipped = Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "causal-loop-diagram")).SelectMany(folder => Directory.GetFiles(folder, "*.cld"));
        return [.. Sorted(fixtures), .. Sorted(examples), .. Sorted(shipped)];

        static IEnumerable<string> Sorted(IEnumerable<string> paths) =>
            paths.Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
    }
}
