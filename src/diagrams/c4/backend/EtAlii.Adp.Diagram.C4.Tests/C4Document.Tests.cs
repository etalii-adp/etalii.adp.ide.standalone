using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The textual guarantee everything else rests on: a document ADP did not change comes back
/// byte-identical, and one it did change differs only in the lines the edit touched
/// (c4-diagrams Requirements 3.1, 3.2).
/// </summary>
public class C4DocumentTests
{
    public static TheoryData<string> Corpus() =>
    [
        "minimal.dsl",
        "comments-everywhere.dsl",
        "crlf-line-endings.dsl",
        "lf-line-endings.dsl",
        "no-trailing-newline.dsl",
        "unusual-indentation.dsl",
        "unmodelled-constructs.dsl",
        "deployment-nested.dsl",
        "dynamic-interactions.dsl",
        "big-bank-plc.dsl",
        "aws-deployment.dsl",
        "crowded-component-view.dsl",
    ];

    private static string Read(string name) => File.ReadAllText(IoPath.Combine("Fixtures", name));

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryFixture_RoundTripsByteForByte(string name)
    {
        // Act.
        var text = Read(name);

        // Assert.
        Assert.Equal(text, C4Document.Parse(text).ToText());
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void EveryFixture_KeepsItsOwnLineEndings(string name)
    {
        // Arrange.
        var text = Read(name);

        var document = C4Document.Parse(text);

        // Act and assert, step by step.
        var expected = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        Assert.Equal(expected, document.Newline);
    }

    [Fact]
    public void AFileWithoutATrailingNewline_StillHasNoneAfterARoundTrip()
    {
        // Act.
        var document = C4Document.Parse(Read("no-trailing-newline.dsl"));

        // Assert.
        Assert.False(document.EndsWithNewline);
        Assert.False(document.ToText().EndsWith('\n'));
    }

    [Fact]
    public void AnEditChangesOnlyTheLineItTouches()
    {
        // Arrange.
        var text = Read("minimal.dsl");
        var document = C4Document.Parse(text);
        var target = document.CodeLines.First(line => line.Code.Contains("softwareSystem", StringComparison.Ordinal));

        document.ReplaceLine(target.Number, target.Text.Replace("\"System\"", "\"Renamed\"", StringComparison.Ordinal));

        // Act and assert, step by step.
        var before = text.Split('\n');
        var after = document.ToText().Split('\n');
        Assert.Equal(before.Length, after.Length);
        var differing = before.Zip(after).Select((pair, index) => (pair, index)).Where(x => x.pair.First != x.pair.Second).ToArray();
        Assert.Single(differing);
        Assert.Contains("Renamed", differing[0].pair.Second, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("one line", "one line")]
    [InlineData("a\nb", "a\nb")]
    [InlineData("a\r\nb\r\n", "a\r\nb\r\n")]
    [InlineData("\n", "\n")]
    [InlineData("a\n\n\nb\n", "a\n\n\nb\n")]
    public void DegenerateTexts_RoundTripToo(string text, string expected)
    {
        // Arrange, act and assert.
        Assert.Equal(expected, C4Document.Parse(text).ToText());
    }

    [Fact]
    public void MixedLineEndings_ReuseTheDominantOne_AndTheOthersSurviveUntouched()
    {
        // Arrange.
        // A document edited on two platforms is an ordinary thing to find in a repository;
        // rewriting it wholesale to one style would be exactly the diff Requirement 3.1 forbids.
        var text = "a\r\nb\r\nc\nd\r\n";

        // Act.
        var document = C4Document.Parse(text);

        // Assert.
        Assert.Equal("\r\n", document.Newline);
        Assert.Equal(4, document.Lines.Count);
        // The half this test was named for and never checked. It asserted the dominant
        // terminator was *detected* and stopped there, so the writer rewriting every line to it
        // - which is what it did - passed unnoticed.
        Assert.Equal(text, document.ToText());
    }

    [Theory]
    [InlineData("a\r\nb\r\nc\nd\r\n")]
    [InlineData("a\nb\nc\r\nd\n")]
    [InlineData("a\r\nb\n")]
    [InlineData("a\nb\r\n")]
    public void ADocumentWithMixedLineEndings_RoundTripsByteForByte(string text)
    {
        // Act and assert.
        // A document edited on two platforms is an ordinary thing to find in a repository, and
        // ADP's whole claim on the Structurizr DSL is that it edits somebody else's format by
        // the line and changes nothing it did not touch. Rewriting every terminator makes the
        // first save a whole-file diff (Requirement 3.1).
        Assert.Equal(text, C4Document.Parse(text).ToText());
    }

    [Fact]
    public void EditingOneLineOfAMixedDocument_LeavesEveryOtherTerminatorAlone()
    {
        // Arrange.
        var document = C4Document.Parse("a\r\nb\nc\r\n");

        // Act.
        document.ReplaceLine(1, "changed");

        // Assert.
        // The edited line keeps its own terminator too - it was replaced, not re-terminated.
        Assert.Equal("changed\r\nb\nc\r\n", document.ToText());
    }

    [Fact]
    public void ALineInsertedIntoAMixedDocument_TakesTheDominantTerminator()
    {
        // Arrange.
        // A new line has no terminator of its own to keep, so it gets the document's prevailing
        // style - which is what `Newline` was always for.
        var document = C4Document.Parse("a\r\nb\nc\r\n");

        // Act.
        document.InsertLine(2, "inserted");

        // Assert.
        Assert.Equal("a\r\ninserted\r\nb\nc\r\n", document.ToText());
    }

    // ---- what the parsers read ---------------------------------------------------------

    [Theory]
    [InlineData("model {", "model {")]
    [InlineData("    model { // trailing", "model {")]
    [InlineData("  # whole line", "")]
    [InlineData("person \"A\" \"Uses // not a comment\"", "person \"A\" \"Uses // not a comment\"")]
    [InlineData("person \"A\" \"A # hash\"", "person \"A\" \"A # hash\"")]
    [InlineData("url \"https://example.com\"", "url \"https://example.com\"")]
    public void ALinesCode_DropsCommentsButNotWhatLooksLikeOneInsideAName(string text, string expected)
    {
        // Arrange, act and assert.
        Assert.Equal(expected, new C4Line(text, 1).Code);
    }

    [Fact]
    public void TheCommentsFixture_HasCodeOnPreciselyTheLinesThatAreNotComments()
    {
        // Act.
        var document = C4Document.Parse(Read("comments-everywhere.dsl"));

        // Assert.
        // The five leading comment lines and the trailing one carry no code at all.
        Assert.True(document.CodeLines.First().IsBlank);
        Assert.True(document.CodeLines.Last(line => line.Text.Length > 0).IsBlank);
        // ...while the workspace line does, with its comment gone.
        Assert.Contains(document.CodeLines, line => line.Code.StartsWith("workspace ", StringComparison.Ordinal));
        Assert.Contains(document.CodeLines, line => line.Code == "model {");
    }
}
