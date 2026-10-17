using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>One owner of the <c>.adp</c> registration: what it reads, what it writes back, and its cache (code-structure FR-061, T173).</summary>
public class AdpRegistrationTests
{
    [Fact]
    public void Parse_ReadsTheMimeLineTheHeadersAndTheLayout()
    {
        // Act.
        var registration = AdpRegistration.Parse("application/vnd.vendor.type\r\nbody: ../shared.ttl\r\nview: classes\r\nlayout:\r\n  a: 10 20\r\n  b: -1.5 3\r\n");

        // Assert.
        Assert.Equal("application/vnd.vendor.type", registration.MimeType);
        Assert.Equal("../shared.ttl", registration.Body);
        Assert.Equal("classes", registration.View);
        Assert.Equal(new RegistrationPosition(10, 20), registration.Layout["a"]);
        Assert.Equal(new RegistrationPosition(-1.5, 3), registration.Layout["b"]);
    }

    [Fact]
    public void Parse_OfAnEmptyFile_HasNoMimeTypeAndNothingElse()
    {
        // Act.
        var registration = AdpRegistration.Parse("");

        // Assert.
        Assert.Null(registration.MimeType);
        Assert.Null(registration.Body);
        Assert.Null(registration.View);
        Assert.Empty(registration.Layout);
    }

    [Fact]
    public void Parse_StopsLookingForHeadersAtTheFirstOtherLine()
    {
        // Act: a view after prose is prose, not a header.
        var registration = AdpRegistration.Parse("type/x\nbody: a.txt\nsome prose\nview: hidden\n");

        // Assert.
        Assert.Equal("a.txt", registration.Body);
        Assert.Null(registration.View);
    }

    [Fact]
    public void Parse_SkipsBlankLinesBetweenHeaders_AndStopsAfterEightLines()
    {
        // Arrange: seven blanks and then a header is the eighth line after the MIME line; one more
        // blank pushes it to the ninth, past the scan.
        var within = "type/x\n" + string.Concat(Enumerable.Repeat("\n", 7)) + "body: near.txt\n";
        var beyond = "type/x\n" + string.Concat(Enumerable.Repeat("\n", 8)) + "body: far.txt\n";

        // Act and assert.
        Assert.Equal("near.txt", AdpRegistration.Parse(within).Body);
        Assert.Null(AdpRegistration.Parse(beyond).Body);
    }

    [Fact]
    public void Parse_ToleratesBlanksInTheLayout_AndEndsTheBlockAtTheFirstLineThatIsNoEntry()
    {
        // Act: a malformed entry is not fatal - it is a line that is no entry, so the block ends there.
        var registration = AdpRegistration.Parse("type/x\nlayout:\n  good: 1 2\n\n  later: 3 4\n  bad: one 2\n  after: 5 6\n");

        // Assert.
        Assert.Equal(["good", "later"], registration.Layout.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void WithLayout_ReplacesTheBlock_AndKeepsEveryOtherByte()
    {
        // Arrange: CRLF above, LF and a trailing line below - neither may change.
        const string text = "type/x\r\nbody: a.txt\r\nlayout:\r\n  old: 1 1\r\ntrailing: kept\nlast";
        var registration = AdpRegistration.Parse(text);

        // Act.
        var written = registration.WithLayout(new Dictionary<string, RegistrationPosition> { ["b"] = new(2, 3.25), ["a"] = new(0.5, 1) });

        // Assert.
        Assert.Equal("type/x\r\nbody: a.txt\r\nlayout:\r\n  a: 0.5 1\r\n  b: 2 3.25\r\ntrailing: kept\nlast", written);
    }

    [Fact]
    public void WithLayout_WithoutABlock_PutsItAfterTheHeaderRegion()
    {
        // Arrange.
        var registration = AdpRegistration.Parse("type/x\nbody: a.txt\n\nprose\n");

        // Act.
        var written = registration.WithLayout(new Dictionary<string, RegistrationPosition> { ["a"] = new(1, 2) });

        // Assert.
        Assert.Equal("type/x\nbody: a.txt\n\nlayout:\r\n  a: 1 2\r\nprose\n", written);
    }

    [Fact]
    public void WithLayout_WithNoPositions_RemovesTheBlock()
    {
        // Arrange.
        var registration = AdpRegistration.Parse("type/x\nlayout:\n  a: 1 2\n");

        // Act and assert.
        Assert.Equal("type/x\n", registration.WithLayout(new Dictionary<string, RegistrationPosition>()));
    }

    [Fact]
    public void WithLayout_ReadBack_GivesThePositionsWritten()
    {
        // Arrange.
        var positions = new Dictionary<string, RegistrationPosition> { ["x"] = new(-4, 8.125), ["y"] = new(0, 0) };

        // Act.
        var reread = AdpRegistration.Parse(AdpRegistration.Parse("type/x\r\n").WithLayout(positions));

        // Assert.
        Assert.Equal(positions, reread.Layout);
    }

    [Fact]
    public void Compose_WritesTheMimeLine_AndABodyHeaderOnlyWhenOneIsNamed()
    {
        // Act and assert.
        Assert.Equal("type/x\r\n", AdpRegistration.Compose("type/x"));
        Assert.Equal("type/x\r\nbody: shared.ttl\r\n", AdpRegistration.Compose("type/x", "shared.ttl"));
    }

    [Fact]
    public void WithBodyFileName_RenamesTheHeadersLastSegment_AndKeepsItsFolderAndTerminator()
    {
        // Arrange.
        var registration = AdpRegistration.Parse("type/x\r\nview: classes\r\n  body: ../models/old.ttl\r\nlayout:\r\n");

        // Act.
        var renamed = registration.WithBodyFileName("OLD.ttl", "new.ttl");

        // Assert: matched without regard to case, the folder part and the CR kept.
        Assert.Equal("type/x\r\nview: classes\r\n  body: ../models/new.ttl\r\nlayout:\r\n", renamed);
    }

    [Fact]
    public void WithBodyFileName_LeavesTheTextAsItWas_WhenTheHeaderNamesAnotherFile_OrStandsBelowProse()
    {
        // Arrange.
        var other = AdpRegistration.Parse("type/x\nbody: kept.ttl\n");
        var belowProse = AdpRegistration.Parse("type/x\nprose\nbody: old.ttl\n");

        // Act and assert.
        Assert.Equal(other.Text, other.WithBodyFileName("old.ttl", "new.ttl"));
        Assert.Equal(belowProse.Text, belowProse.WithBodyFileName("old.ttl", "new.ttl"));
    }

    [Fact]
    public async Task Read_WhileTheFileIsUnchanged_AnswersFromTheCache_AndAfterAWrite_ReadsAgain()
    {
        // Arrange.
        using var folder = TestFolder.Create("adp-registration");
        var path = folder.Combine("a.adp");
        await File.WriteAllTextAsync(path, "type/one\n", TestContext.Current.CancellationToken);

        // Act.
        var first = AdpRegistration.Read(path);
        var again = AdpRegistration.Read(path);
        await File.WriteAllTextAsync(path, "type/two\nbody: b.txt\n", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(1));
        var changed = AdpRegistration.Read(path);

        // Assert.
        Assert.NotNull(first);
        Assert.Same(first, again);
        Assert.Equal("type/two", changed?.MimeType);
        Assert.Equal("b.txt", changed?.Body);
    }

    [Fact]
    public async Task Read_SeesAWriteThatKeepsTheModificationTime_WhenTheLengthChanges()
    {
        // Arrange: a write inside the clock's resolution keeps the stamp; the length still tells.
        using var folder = TestFolder.Create("adp-registration");
        var path = folder.Combine("a.adp");
        await File.WriteAllTextAsync(path, "type/one\n", TestContext.Current.CancellationToken);
        var stamp = File.GetLastWriteTimeUtc(path);
        _ = AdpRegistration.Read(path);

        // Act.
        await File.WriteAllTextAsync(path, "type/longer\n", TestContext.Current.CancellationToken);
        File.SetLastWriteTimeUtc(path, stamp);
        var changed = AdpRegistration.Read(path);

        // Assert.
        Assert.Equal("type/longer", changed?.MimeType);
    }

    [Fact]
    public void Read_OfAFileThatIsNotThere_IsNull()
    {
        // Arrange.
        using var folder = TestFolder.Create("adp-registration");

        // Act and assert.
        Assert.Null(AdpRegistration.Read(folder.Combine("missing.adp")));
    }
}
