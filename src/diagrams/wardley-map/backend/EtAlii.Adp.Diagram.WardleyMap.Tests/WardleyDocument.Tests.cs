using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyDocumentTests
{
    /// <summary>
    /// Every fixture in the corpus, by path. Task 2 assembled these and certified each one
    /// against the real OnlineWardleyMaps parser; this class is the first consumer, and the
    /// round-trip test below is the reason the corpus exists at all.
    /// </summary>
    public static TheoryData<string> Corpus
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(FixturesPath, "*.owm").Order(StringComparer.Ordinal))
            {
                data.Add(IoPath.GetFileName(path));
            }

            return data;
        }
    }

    private static string FixturesPath => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ReadFixture(string name) => File.ReadAllText(IoPath.Combine(FixturesPath, name));

    [Fact]
    public void Corpus_IsNotEmpty_SoAnEmptyFolderCannotLookLikeAPassingSuite()
    {
        // Arrange, act and assert. Every test below is a Theory over the corpus, so a corpus
        // that failed to copy to the output folder would report as green rather than as
        // nothing having run.
        Assert.NotEmpty(Corpus);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ToText_ReturnsTheInputByteForByte_WhenNothingWasEdited(string name)
    {
        // Arrange.
        var text = ReadFixture(name);

        // Act.
        var document = WardleyDocument.Parse(text);

        // Assert. Requirement 3.1, and this module's headline correctness property.
        Assert.Equal(text, document.ToText());
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void CodeLines_AreNumberedFromOne(string name)
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture(name));

        // Act.
        var lines = document.CodeLines.ToArray();

        // Assert. 1-based, to match DiagramProblemLineLocation.
        Assert.Equal(document.Lines.Count, lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            Assert.Equal((uint)(index + 1), lines[index].Number);
            Assert.Equal(document.Lines[index], lines[index].Text);
        }
    }

    [Fact]
    public void Parse_PreservesCrlf_AndTheLfPairIsGenuinelyDifferent()
    {
        // Arrange. The pair exists to prove the writer preserves what it was given rather than
        // normalising; if .gitattributes ever stopped protecting these bytes, this fails.
        var crlf = ReadFixture("crlf-line-endings.owm");
        var lf = ReadFixture("lf-line-endings.owm");

        // Act.
        var fromCrlf = WardleyDocument.Parse(crlf);
        var fromLf = WardleyDocument.Parse(lf);

        // Assert.
        Assert.Equal("\r\n", fromCrlf.Newline);
        Assert.Equal("\n", fromLf.Newline);
        Assert.NotEqual(crlf, lf);
        Assert.Equal(fromCrlf.Lines, fromLf.Lines);
    }

    [Fact]
    public void Parse_RemembersAMissingFinalNewline()
    {
        // Arrange.
        var text = ReadFixture("no-trailing-newline.owm");

        // Act.
        var document = WardleyDocument.Parse(text);

        // Assert. The case a naive line-splitter silently "fixes".
        Assert.False(document.EndsWithNewline);
        Assert.Equal(text, document.ToText());
    }

    [Fact]
    public void Parse_KeepsBlankLineRuns_RatherThanCompactingThem()
    {
        // Arrange.
        var text = ReadFixture("blank-line-runs.owm");

        // Act.
        var document = WardleyDocument.Parse(text);

        // Assert.
        Assert.Contains("", document.Lines);
        Assert.Equal(text, document.ToText());
    }

    [Fact]
    public void ReplaceLine_ChangesExactlyOneLine_AndLeavesEveryOtherByteAlone()
    {
        // Arrange. A map full of comments and blank lines is where a regenerating writer does
        // its damage, so the assertion is against that file rather than a minimal one.
        var text = ReadFixture("comments-everywhere.owm");
        var document = WardleyDocument.Parse(text);
        var before = document.Lines.ToArray();
        const uint target = 6;

        // Act.
        document.ReplaceLine(target, "component Cup of Tea [0.79, 0.42] // a trailing comment on an element");

        // Assert. Requirement 3.2 - the whole of it.
        var after = document.Lines.ToArray();
        Assert.Equal(before.Length, after.Length);
        for (var index = 0; index < after.Length; index++)
        {
            if (index == (int)target - 1)
            {
                Assert.NotEqual(before[index], after[index]);
                continue;
            }

            Assert.Equal(before[index], after[index]);
        }
    }

    [Fact]
    public void ReplaceLine_KeepsTheDocumentsOwnNewline_RatherThanThePlatformsOne()
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture("crlf-line-endings.owm"));

        // Act.
        document.ReplaceLine(1, "title Edited");

        // Assert. An edit on a CRLF file must not smuggle LF into it.
        Assert.Contains("title Edited\r\n", document.ToText(), StringComparison.Ordinal);
        Assert.DoesNotContain("title Edited\n\n", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void InsertLine_PlacesTheTextAtThatNumber_AndShiftsTheRestDown()
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture("minimal.owm"));
        var second = document.Lines[1];

        // Act.
        document.InsertLine(2, "// inserted");

        // Assert.
        Assert.Equal("// inserted", document.Lines[1]);
        Assert.Equal(second, document.Lines[2]);
    }

    [Fact]
    public void InsertLine_AcceptsOnePastTheEnd_SoAppendingIsNotASpecialCase()
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture("minimal.owm"));
        var count = document.Lines.Count;

        // Act.
        document.InsertLine((uint)count + 1, "// appended");

        // Assert.
        Assert.Equal(count + 1, document.Lines.Count);
        Assert.Equal("// appended", document.Lines[count]);
    }

    [Fact]
    public void RemoveLines_RemovesTheInclusiveRange()
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture("tea-shop.owm"));
        var count = document.Lines.Count;
        var following = document.Lines[6];

        // Act.
        document.RemoveLines(4, 6);

        // Assert.
        Assert.Equal(count - 3, document.Lines.Count);
        Assert.Equal(following, document.Lines[3]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(99u)]
    public void ReplaceLine_RefusesALineTheDocumentDoesNotHave(uint number)
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture("minimal.owm"));

        // Act and assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.ReplaceLine(number, "x"));
    }

    [Fact]
    public void RemoveLines_RefusesARangeThatRunsBackwards()
    {
        // Arrange.
        var document = WardleyDocument.Parse(ReadFixture("tea-shop.owm"));

        // Act and assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => document.RemoveLines(5, 3));
    }

    [Fact]
    public void Parse_HandlesAnEmptyString()
    {
        // Act.
        var document = WardleyDocument.Parse("");

        // Assert. An empty body is not a valid map, but it must not crash the reader either -
        // Requirement 2.4 opens a missing sibling as an empty map.
        Assert.Equal("", document.ToText());
        Assert.False(document.EndsWithNewline);
    }

    [Fact]
    public void Parse_HandlesASingleNewline()
    {
        // Act.
        var document = WardleyDocument.Parse("\n");

        // Assert.
        Assert.Equal("\n", document.ToText());
        Assert.True(document.EndsWithNewline);
    }

    [Fact]
    public void Parse_TreatsMixedTerminatorsAsTheDominantOne_RatherThanRefusing()
    {
        // Arrange. A document edited on two platforms is a normal thing to find in a repo.
        const string text = "title Mixed\r\nanchor A [0.9, 0.5]\r\ncomponent B [0.5, 0.5]\n";

        // Act.
        var document = WardleyDocument.Parse(text);

        // Assert. It round-trips regardless, because every line kept its own bytes; Newline
        // only decides what a *new* line is terminated with.
        Assert.Equal("\r\n", document.Newline);
        Assert.Equal(text, document.ToText());
    }

    [Fact]
    public void Parse_RejectsNull()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => WardleyDocument.Parse(null!));
    }
}
