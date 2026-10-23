using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests.Parity;

/// <summary>
/// The parity corpus of the .NET dependency graph: the solutions among the test project's <c>Fixtures/</c>,
/// the module's <c>examples/</c> and the shipped copies under <c>src/examples/diagrams/dotnet-dependency-graph/</c>,
/// and the fixture NuGet cache their package descriptions are read from.
/// </summary>
internal static class DotNetCorpus
{
    /// <summary>This test project's name.</summary>
    public const string TestProject = "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests";

    /// <summary>The module's folder, <c>src/diagrams/dotnet-dependency-graph</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "dotnet-dependency-graph");

    /// <summary>The repository's <c>src</c> folder, which every corpus path is relative to.</summary>
    public static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The test project's fixture NuGet cache, never the machine's.</summary>
    public static string FixtureCache => Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures", "cache");

    /// <summary>The corpus, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Solutions()
    {
        return
        [
            .. In(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures")),
            .. In(Path.Combine(ModuleFolder, "examples")),
            .. In(Path.Combine(SourceFolder, "examples", "diagrams", "dotnet-dependency-graph")),
        ];

        static IEnumerable<string> In(string folder) =>
            Directory.Exists(folder)
                ? Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(path => Path.GetExtension(path) is ".sln" or ".slnx")
                    .Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/'))
                    .Order(StringComparer.Ordinal)
                : [];
    }
}
