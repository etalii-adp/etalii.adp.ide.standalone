using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// An OnlineWardleyMaps document held as the core <see cref="LineDocument"/>, and the 1-based
/// line edits the module makes on it.
/// </summary>
public class WardleyLinesTests
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
        var document = LineDocument.Parse(text);

        // Assert. Requirement 3.1, and this module's headline correctness property.
        Assert.Equal(text, document.Text);
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void CodeLines_AreNumberedFromOne(string name)
    {
        // Arrange.
        var document = LineDocument.Parse(ReadFixture(name));

        // Act.
        var lines = WardleyParser.CodeLines(document).ToArray();

        // Assert. 1-based, to match DiagramProblemLineLocation.
        Assert.Equal(document.Lines.Count, lines.Length);
        for (var index = 0; index < lines.Length; index++)
        {
            Assert.Equal((uint)(index + 1), lines[index].Number);
            Assert.Equal(document.Lines[index].Text, lines[index].Text);
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
        var fromCrlf = LineDocument.Parse(crlf);
        var fromLf = LineDocument.Parse(lf);

        // Assert.
        Assert.Equal("\r\n", fromCrlf.DominantEnding);
        Assert.Equal("\n", fromLf.DominantEnding);
        Assert.NotEqual(crlf, lf);
        Assert.Equal(fromCrlf.Lines.Select(line => line.Text), fromLf.Lines.Select(line => line.Text));
    }

    [Fact]
    public void Parse_RemembersAMissingFinalNewline()
    {
        // Arrange.
        var text = ReadFixture("no-trailing-newline.owm");

        // Act.
        var document = LineDocument.Parse(text);

        // Assert. The case a naive line-splitter silently "fixes".
        Assert.Equal("", document.Lines[^1].Ending);
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void Parse_KeepsBlankLineRuns_RatherThanCompactingThem()
    {
        // Arrange.
        var text = ReadFixture("blank-line-runs.owm");

        // Act.
        var document = LineDocument.Parse(text);

        // Assert.
        Assert.Contains(document.Lines, line => line.Text.Length == 0);
        Assert.Equal(text, document.Text);
    }

    [Fact]
    public void ReplaceLine_ChangesExactlyOneLine_AndLeavesEveryOtherByteAlone()
    {
        // Arrange. A map full of comments and blank lines is where a regenerating writer does
        // its damage, so the assertion is against that file rather than a minimal one.
        var text = ReadFixture("comments-everywhere.owm");
        var document = LineDocument.Parse(text);
        var before = document.Lines.ToArray();
        const uint target = 6;

        // Act.
        WardleyWriter.ReplaceLine(document, target, "component Cup of Tea [0.79, 0.42] // a trailing comment on an element");

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
        var document = LineDocument.Parse(ReadFixture("crlf-line-endings.owm"));

        // Act.
        WardleyWriter.ReplaceLine(document, 1, "title Edited");

        // Assert. An edit on a CRLF file must not smuggle LF into it.
        Assert.Contains("title Edited\r\n", document.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("title Edited\n\n", document.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void InsertLine_PlacesTheTextAtThatNumber_AndShiftsTheRestDown()
    {
        // Arrange.
        var document = LineDocument.Parse(ReadFixture("minimal.owm"));
        var second = document.Lines[1];

        // Act.
        WardleyWriter.InsertLine(document, 2, "// inserted");

        // Assert.
        Assert.Equal("// inserted", document.Lines[1].Text);
        Assert.Equal(second, document.Lines[2]);
    }

    [Fact]
    public void InsertLine_AcceptsOnePastTheEnd_SoAppendingIsNotASpecialCase()
    {
        // Arrange.
        var document = LineDocument.Parse(ReadFixture("minimal.owm"));
        var count = document.Lines.Count;

        // Act.
        WardleyWriter.InsertLine(document, (uint)count + 1, "// appended");

        // Assert.
        Assert.Equal(count + 1, document.Lines.Count);
        Assert.Equal("// appended", document.Lines[count].Text);
    }

    [Fact]
    public void RemoveLine_RemovesThatLine_AndShiftsTheRestUp()
    {
        // Arrange.
        var document = LineDocument.Parse(ReadFixture("tea-shop.owm"));
        var count = document.Lines.Count;
        var following = document.Lines[5];

        // Act.
        WardleyWriter.RemoveLine(document, 5);

        // Assert.
        Assert.Equal(count - 1, document.Lines.Count);
        Assert.Equal(following, document.Lines[4]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(99u)]
    public void ReplaceLine_RefusesALineTheDocumentDoesNotHave(uint number)
    {
        // Arrange.
        var document = LineDocument.Parse(ReadFixture("minimal.owm"));

        // Act and assert.
        Assert.Throws<ArgumentOutOfRangeException>(() => WardleyWriter.ReplaceLine(document, number, "x"));
    }
}
