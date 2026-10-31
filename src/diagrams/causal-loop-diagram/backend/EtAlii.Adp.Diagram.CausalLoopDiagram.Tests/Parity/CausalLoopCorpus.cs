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
        return [.. RepositoryPaths.SortedRelativeToSource(fixtures), .. RepositoryPaths.SortedRelativeToSource(examples), .. RepositoryPaths.SortedRelativeToSource(shipped)];
    }

    /// <summary>The most variables a document of <see cref="WalkedElementByElement"/> has.</summary>
    private const int MostWalkedVariables = 30;

    /// <summary>
    /// The corpus, less its documents of more than <see cref="MostWalkedVariables"/> variables: what the tests that
    /// walk every element, and every pair of variables, read.
    /// </summary>
    /// <remarks>
    /// Those tests drive the providers once per element and once per ordered pair of variables, and every step reads,
    /// writes and judges the whole document, cycles included, so their cost grows faster than the square of its size.
    /// The examples redrawn from published figures - thirty-four variables for life insurance and forty-nine for the
    /// UK cod population, each shipped twice - took one such document past twenty minutes and the backend tests from
    /// about twenty minutes to over two hours, while showing nothing about the parity of an entry that the smaller
    /// documents do not. They stay in the corpus of every other test, and their own claims are held in Examples.Tests.
    /// </remarks>
    public static IReadOnlyList<string> WalkedElementByElement() =>
        [.. Paths().Where(relative => CausalLoopBody.Parse(File.ReadAllText(Path.Combine(SourceFolder, relative))).Read().Variables.Count <= MostWalkedVariables)];
}
