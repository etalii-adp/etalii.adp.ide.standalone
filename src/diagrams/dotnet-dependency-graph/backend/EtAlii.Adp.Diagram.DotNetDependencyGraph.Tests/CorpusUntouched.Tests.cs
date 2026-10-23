using EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests.Parity;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// Nothing under a solution is ever written (the Reliability requirement of
/// dotnet-dependency-graph-disl-fbl): every file of the transcript's corpus is hashed before and
/// after the transcript is generated, and only a registration may differ, appear or go.
/// </summary>
/// <remarks>
/// The corpus is the transcript's: the test project's <c>Fixtures/</c>, the module's
/// <c>examples/</c> and the shipped copies under <c>src/examples/diagrams/dotnet-dependency-graph/</c>,
/// read where the transcript reads them, in the source tree rather than beside the test binary.
/// </remarks>
public sealed class CorpusUntouchedTests
{
    [Fact]
    public async Task TheTranscriptRun_ChangesNoFileOfTheCorpus_ButARegistration()
    {
        // Arrange.
        var cancellationToken = TestContext.Current.CancellationToken;
        var before = await HashesAsync(cancellationToken);

        // Act.
        _ = await TranscriptHarness.GenerateAsync(new DotNetTranscript(), cancellationToken);
        var after = await HashesAsync(cancellationToken);

        // Assert.
        var changed = before.Keys.Union(after.Keys, StringComparer.Ordinal)
            .Where(path => before.GetValueOrDefault(path) != after.GetValueOrDefault(path))
            .Where(path => !string.Equals(Path.GetExtension(path), ".adp", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(before.Count > 0, "The corpus holds no file, so nothing was checked.");
        Assert.True(changed.Count == 0, $"The transcript run changed {changed.Count} file(s) of the corpus: {string.Join(", ", changed)}");
    }

    /// <summary>The SHA-256 of every file of the corpus, by its path relative to <c>src/</c>.</summary>
    private static async Task<Dictionary<string, string>> HashesAsync(CancellationToken cancellationToken)
    {
        var source = RepositoryPaths.Under("src");
        string[] folders =
        [
            RepositoryPaths.Under("src", "diagrams", "dotnet-dependency-graph", "backend", "EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests", "Fixtures"),
            RepositoryPaths.Under("src", "diagrams", "dotnet-dependency-graph", "examples"),
            Path.Combine(source, "examples", "diagrams", "dotnet-dependency-graph"),
        ];

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in folders.Where(Directory.Exists).SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)))
        {
            hashes[Path.GetRelativePath(source, path).Replace('\\', '/')] = TranscriptText.Sha256(await File.ReadAllBytesAsync(path, cancellationToken));
        }

        return hashes;
    }
}
