using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

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
    public void SetPosition_AddsTheBlockAfterTheHeaders_AndLeavesThemByteIdentical()
    {
        // Arrange.
        var header = "databricks/job\r\nbody: job.yml\r\nview: nightly\r\n";
        var adp = WriteAdp(header);

        // Act.
        var prior = RegistrationLayout.SetPosition(adp, "ingest", new RegistrationPosition(120, 240));

        // Assert.
        Assert.Null(prior);
        var text = File.ReadAllText(adp);
        Assert.StartsWith(header, text, StringComparison.Ordinal);
        Assert.Equal(header + "layout:\r\n  ingest: 120 240\r\n", text);
        var read = Assert.Single(RegistrationLayout.Read(adp));
        Assert.Equal("ingest", read.Key);
        Assert.Equal(new RegistrationPosition(120, 240), read.Value);
    }

    [Fact]
    public void SetPosition_OnAnExistingEntry_ReplacesIt_AndAnswersWithThePrior()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        RegistrationLayout.SetPosition(adp, "ingest", new RegistrationPosition(10, 20));

        // Act.
        var prior = RegistrationLayout.SetPosition(adp, "ingest", new RegistrationPosition(30.5, -40));

        // Assert.
        // The prior entry is exactly what an undoable command needs for its inverse.
        Assert.Equal(new RegistrationPosition(10, 20), prior);
        Assert.Equal(new RegistrationPosition(30.5, -40), RegistrationLayout.Read(adp)["ingest"]);
    }

    [Fact]
    public void RemovePosition_RemovingTheLastEntry_RemovesTheBlockItself()
    {
        // Arrange.
        var header = "databricks/job\r\nbody: job.yml\r\n";
        var adp = WriteAdp(header);
        RegistrationLayout.SetPosition(adp, "ingest", new RegistrationPosition(1, 2));

        // Act.
        RegistrationLayout.RemovePosition(adp, "ingest");

        // Assert.
        // No entries, no block: the file returns to its pre-layout bytes.
        Assert.Equal(header, File.ReadAllText(adp));
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
    public void Replace_WritesEveryPosition_AndAnEmptySetRemovesTheBlock()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        RegistrationLayout.SetPosition(adp, "gone", new RegistrationPosition(9, 9));

        // Act.
        RegistrationLayout.Replace(adp, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal) { ["a"] = new(1, 2), ["b"] = new(3, 4) });

        // Assert: exactly the new set, under the untouched headers.
        Assert.Equal(["a", "b"], RegistrationLayout.Read(adp).Keys.Order(StringComparer.Ordinal));
        Assert.StartsWith("databricks/job\r\nbody: job.yml\r\n", File.ReadAllText(adp), StringComparison.Ordinal);

        // Act: the empty set.
        RegistrationLayout.Replace(adp, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal));

        // Assert.
        Assert.Equal("databricks/job\r\nbody: job.yml\r\n", File.ReadAllText(adp));
    }

    [Fact]
    public void Prune_DropsStaleEntries_OnTheNextWrite()
    {
        // Arrange.
        var adp = WriteAdp("databricks/job\r\nbody: job.yml\r\n");
        RegistrationLayout.SetPosition(adp, "kept", new RegistrationPosition(1, 1));
        RegistrationLayout.SetPosition(adp, "stale", new RegistrationPosition(2, 2));

        // Act.
        RegistrationLayout.Prune(adp, new HashSet<string>(["kept"], StringComparer.Ordinal));

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
    public void WriteAndRead_RoundTrip_ThroughMixedHeaderContent()
    {
        // Arrange.
        // Headers, a blank, and a later free-form line: the block lands after the header
        // region and everything else keeps its bytes.
        var adp = WriteAdp("generic/timeline\r\nbody: plan.tml\r\n\r\nsome trailing note\r\n");

        // Act.
        RegistrationLayout.SetPosition(adp, "aaa", new RegistrationPosition(12.25, 8));

        // Assert.
        var text = File.ReadAllText(adp);
        Assert.Contains("layout:\r\n  aaa: 12.25 8\r\n", text, StringComparison.Ordinal);
        Assert.Contains("some trailing note", text, StringComparison.Ordinal);
        Assert.StartsWith("generic/timeline\r\nbody: plan.tml\r\n", text, StringComparison.Ordinal);
        Assert.Equal(new RegistrationPosition(12.25, 8), RegistrationLayout.Read(adp)["aaa"]);
    }
}
