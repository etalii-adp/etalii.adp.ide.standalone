using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests.Parity;

/// <summary>
/// The SPARQL query diagram's corpus: every <c>.rq</c> under <c>src/</c> with the registration it is
/// opened through (sparql-disl-fbl, Requirement 1.2).
/// </summary>
/// <remarks>
/// <para>
/// Three parts, each in ordinal order: the test project's <c>Fixtures/</c>, the eleven it had and
/// the eight added for the findings marked <i>read</i>; the module's <c>examples/</c>; and their
/// shipped copies under <c>src/examples/diagrams/sparql/</c>, three of whose registrations carry a
/// <c>layout:</c> block.
/// </para>
/// <para>
/// A query's registration is the <c>.adp</c> beside it whose <c>body:</c> names it. A fixture has
/// none but <c>second-optional-positioned.rq</c>, and is opened through the bare registration
/// core would write for it.
/// </para>
/// </remarks>
internal static class SparqlCorpus
{
    private const string TestProject = "EtAlii.Adp.Diagram.Sparql.Tests";

    /// <summary>The module's folder, <c>src/diagrams/sparql</c>.</summary>
    public static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "sparql");

    /// <summary>The <c>src/</c> folder, which the corpus's paths are relative to.</summary>
    public static string SourceFolder { get; } = Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The test project's folder.</summary>
    public static string TestFolderPath { get; } = Path.Combine(ModuleFolder, "backend", TestProject);

    /// <summary>The corpus, as <c>.rq</c> paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Documents() =>
    [
        .. Queries(Path.Combine(TestFolderPath, "Fixtures")),
        .. Queries(Path.Combine(ModuleFolder, "examples")),
        .. Queries(Path.Combine(SourceFolder, "examples", "diagrams", "sparql")),
    ];

    /// <summary>The full path of a corpus document.</summary>
    public static string Full(string relative) => Path.Combine(SourceFolder, relative);

    /// <summary>The registration beside <paramref name="query"/> that names it as its body, or null.</summary>
    public static string? RegistrationOf(string query)
    {
        var name = Path.GetFileName(query);
        return Directory.GetFiles(Path.GetDirectoryName(query)!, "*.adp")
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(registration => File.ReadLines(registration).Any(line => line.Trim() == $"body: {name}"));
    }

    private static IEnumerable<string> Queries(string folder) =>
        Directory.GetFiles(folder, "*.rq", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal);
}
