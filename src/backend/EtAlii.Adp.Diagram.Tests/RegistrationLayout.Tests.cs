using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>
/// The `.adp` layout block: authored positions stored beside the headers, never in the body
/// (databricks-diagrams Requirement 7; tech.md's layout-in-.adp rule).
/// </summary>
public class RegistrationLayoutTests : IDisposable
{
    private readonly string _root;

    public RegistrationLayoutTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string WriteAdp(string content)
    {
        var path = IoPath.Combine(_root, "plan.adp");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Read_ARegistrationWithoutABlock_IsEmpty()
    {
        // Arrange & act.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");

        // Assert.
        Assert.Empty(RegistrationLayout.Read(adp));
    }

    [Fact]
    public void Read_AMissingFile_IsEmpty_NotFatal()
    {
        // The block is metadata; its absence must never stop a diagram from opening.
        Assert.Empty(RegistrationLayout.Read(IoPath.Combine(_root, "gone.adp")));
    }

    [Fact]
    public async Task SetPosition_AddsTheBlockAfterTheHeaders_AndLeavesThemByteIdentical()
    {
        // Arrange.
        var header = "databricks/job\r\nbody: job.yml\r\nview: nightly\r\n";
        var adp = WriteAdp(header);

        // Act.
        var prior = await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "ingest", new RegistrationPosition(120, 240), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Null(prior);
        var text = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);
        Assert.StartsWith(header, text, StringComparison.Ordinal);
        Assert.Equal(header + "layout:\r\n  ingest: 120 240\r\n", text);
        var read = Assert.Single(RegistrationLayout.Read(adp));
        Assert.Equal("ingest", read.Key);
        Assert.Equal(new RegistrationPosition(120, 240), read.Value);
    }

    [Fact]
    public async Task SetPosition_OnAnExistingEntry_ReplacesIt_AndAnswersWithThePrior()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "ingest", new RegistrationPosition(10, 20), TestContext.Current.CancellationToken);

        // Act.
        var prior = await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "ingest", new RegistrationPosition(30.5, -40), TestContext.Current.CancellationToken);

        // Assert.
        // The prior entry is exactly what an undoable command needs for its inverse.
        Assert.Equal(new RegistrationPosition(10, 20), prior);
        Assert.Equal(new RegistrationPosition(30.5, -40), RegistrationLayout.Read(adp)["ingest"]);
    }

    [Fact]
    public async Task RemovePosition_RemovingTheLastEntry_RemovesTheBlockItself()
    {
        // Arrange.
        var header = "databricks/job\r\nbody: job.yml\r\n";
        var adp = WriteAdp(header);
        await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "ingest", new RegistrationPosition(1, 2), TestContext.Current.CancellationToken);

        // Act.
        await RegistrationLayout.RemovePositionAsync(new FileWriter(), adp, "ingest", TestContext.Current.CancellationToken);

        // Assert.
        // No entries, no block: the file returns to its pre-layout bytes.
        Assert.Equal(header, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SetPosition_PublishesThroughTheWriterItIsHanded()
    {
        // Arrange. A module's command hands in the host's writer, so its write of a registration
        // takes its turn with every other save of that file.
        var header = "databricks/job\r\nbody: job.yml\r\n";
        var adp = WriteAdp(header);
        var writer = new RecordingWriter();

        // Act.
        await RegistrationLayout.SetPositionAsync(writer, adp, "ingest", new RegistrationPosition(1, 2), TestContext.Current.CancellationToken);

        // Assert: the block went to the writer, and nothing else touched the file.
        (string path, string content) = Assert.Single(writer.Saved);
        Assert.Equal(adp, path);
        Assert.Equal(header + "layout:\r\n  ingest: 1 2\r\n", content);
        Assert.Equal(header, await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Apply_StoredPositionsWin_ElementByElement_AndStaleIdsOverrideNothing()
    {
        // Arrange.
        var computed = new Dictionary<string, RegistrationPosition>
        {
            ["a"] = new(0, 0),
            ["b"] = new(100, 0),
        };
        var stored = new Dictionary<string, RegistrationPosition>
        {
            ["a"] = new(55, 66),
            ["ghost"] = new(9, 9), // no longer in the config: ignored on read (Requirement 7.5)
        };

        // Act.
        var merged = RegistrationLayout.Apply(computed, stored);

        // Assert.
        Assert.Equal(new RegistrationPosition(55, 66), merged["a"]);
        Assert.Equal(new RegistrationPosition(100, 0), merged["b"]);
        Assert.False(merged.ContainsKey("ghost"));
    }

    [Fact]
    public async Task Replace_WritesEveryPosition_AndAnEmptySetRemovesTheBlock()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "gone", new RegistrationPosition(9, 9), TestContext.Current.CancellationToken);

        // Act.
        await RegistrationLayout.ReplaceAsync(new FileWriter(), adp, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal) { ["a"] = new(1, 2), ["b"] = new(3, 4) }, TestContext.Current.CancellationToken);

        // Assert: exactly the new set, under the untouched headers.
        Assert.Equal(["a", "b"], RegistrationLayout.Read(adp).Keys.Order(StringComparer.Ordinal));
        Assert.StartsWith("databricks/job\r\nbody: job.yml\r\n", await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken), StringComparison.Ordinal);

        // Act: the empty set.
        await RegistrationLayout.ReplaceAsync(new FileWriter(), adp, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal("databricks/job\r\nbody: job.yml\r\n", await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Prune_DropsStaleEntries_OnTheNextWrite()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "kept", new RegistrationPosition(1, 1), TestContext.Current.CancellationToken);
        await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "stale", new RegistrationPosition(2, 2), TestContext.Current.CancellationToken);

        // Act.
        await RegistrationLayout.PruneAsync(new FileWriter(), adp, new HashSet<string>(["kept"], StringComparer.Ordinal), TestContext.Current.CancellationToken);

        // Assert.
        var read = Assert.Single(RegistrationLayout.Read(adp));
        Assert.Equal("kept", read.Key);
    }

    [Fact]
    public void Read_AMangledEntry_IsIgnored_AndTheRestSurvive()
    {
        // Arrange.
        // Hand-mangled metadata must never stop a diagram from opening.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\nlayout:\r\n  good: 5 6\r\n  broken entry without numbers\r\n  also-good: 7 8\r\n");

        // Act.
        var read = RegistrationLayout.Read(adp);

        // Assert.
        // The mangled line ends the block per the block's own grammar; what parsed before it
        // stands, and nothing throws.
        Assert.Equal(new RegistrationPosition(5, 6), read["good"]);
    }

    [Fact]
    public async Task WriteAndRead_RoundTrip_ThroughMixedHeaderContent()
    {
        // Arrange.
        // Headers, a blank, and a later free-form line: the block lands after the header
        // region and everything else keeps its bytes.
        var adp = WriteAdp("generic/timeline\r\nbody: plan.tml\r\n\r\nsome trailing note\r\n");

        // Act.
        await RegistrationLayout.SetPositionAsync(new FileWriter(), adp, "aaa", new RegistrationPosition(12.25, 8), TestContext.Current.CancellationToken);

        // Assert.
        var text = await File.ReadAllTextAsync(adp, TestContext.Current.CancellationToken);
        Assert.Contains("layout:\r\n  aaa: 12.25 8\r\n", text, StringComparison.Ordinal);
        Assert.Contains("some trailing note", text, StringComparison.Ordinal);
        Assert.StartsWith("generic/timeline\r\nbody: plan.tml\r\n", text, StringComparison.Ordinal);
        Assert.Equal(new RegistrationPosition(12.25, 8), RegistrationLayout.Read(adp)["aaa"]);
    }

    /// <summary>Records each save it is handed and writes nothing.</summary>
    private sealed class RecordingWriter : IFileWriter
    {
        public List<(string Path, string Content)> Saved { get; } = [];

        public Task SaveAsync(string path, string content, CancellationToken cancellationToken)
        {
            Saved.Add((path, content));
            return Task.CompletedTask;
        }

        public Task SaveAsync(string path, byte[] content, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A layout block is saved as text.");

        public Task DeleteAsync(string path, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A layout block deletes nothing.");
    }
}
