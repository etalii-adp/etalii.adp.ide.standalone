using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// Guards on the corpus itself, not on any code that reads it. Each fixture exists to pin one
/// property, and a well-meaning edit - an editor stripping tabs, git normalising line endings,
/// someone "tidying" a stray brace - would quietly remove the thing it was guarding and leave
/// the round-trip tests still passing against a corpus that no longer tests anything
/// (c4-diagrams task 1).
/// </summary>
public class FixturesTests
{
    private static string Bytes(string name) => File.ReadAllText(IoPath.Combine("Fixtures", name));

    public static TheoryData<string> AllFixtures() =>
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
    ];

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixture_IsPresentAndNonEmpty(string name)
    {
        // Arrange, act and assert.
        Assert.True(File.Exists(IoPath.Combine("Fixtures", name)), $"{name} is missing from the corpus");
        Assert.NotEmpty(Bytes(name));
    }

    [Fact]
    public void TheCrlfAndLfFixtures_DifferOnlyInTheirLineEndings()
    {
        // Arrange and act.
        // The pair is the whole point: same content, two encodings. If git or an editor
        // normalised one of them they would become identical and prove nothing.
        var crlf = Bytes("crlf-line-endings.dsl");
        var lf = Bytes("lf-line-endings.dsl");

        // Assert.
        Assert.Contains("\r\n", crlf, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", lf, StringComparison.Ordinal);
        Assert.Equal(lf, crlf.Replace("\r\n", "\n", StringComparison.Ordinal));
    }

    [Fact]
    public void TheNoTrailingNewlineFixture_StillEndsWithoutOne()
    {
        // Arrange, act and assert.
        Assert.False(Bytes("no-trailing-newline.dsl").EndsWith('\n'));
    }

    [Fact]
    public void TheIndentationFixture_StillMixesTabsAndSpaces()
    {
        // Act.
        var text = Bytes("unusual-indentation.dsl");

        // Assert.
        Assert.Contains("\t", text, StringComparison.Ordinal);
        Assert.Contains("\n          s = softwareSystem", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("//")]
    [InlineData("#")]
    [InlineData("/*")]
    public void TheCommentsFixture_StillCarriesEveryCommentForm(string form)
    {
        // Arrange, act and assert.
        Assert.Contains(form, Bytes("comments-everywhere.dsl"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("!identifiers")]
    [InlineData("!docs")]
    [InlineData("!adrs")]
    [InlineData("styles")]
    [InlineData("theme")]
    [InlineData("configuration")]
    public void TheUnmodelledFixture_StillCarriesTheConstructsRequirement33Protects(string construct)
    {
        // Arrange, act and assert.
        Assert.Contains(construct, Bytes("unmodelled-constructs.dsl"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeploymentFixture_StillNestsAndStillReusesOneContainer()
    {
        // Act.
        var text = Bytes("deployment-nested.dsl");

        // Assert.
        Assert.Contains("infrastructureNode", text, StringComparison.Ordinal);
        // One container deployed twice - the case Requirement 9.3 calls out.
        Assert.Equal(2, text.Split("containerInstance db").Length - 1);
    }

    [Fact]
    public void TheCorpusStillLacksTheRealWorldDocuments_AndSaysSoInItsReadme()
    {
        // Act and assert, step by step.
        // Deliberately asserts the gap rather than hiding it: the canonical Structurizr
        // documents could not be fetched (Fixtures/readme.md records why). When they are
        // added, this test fails and is deleted - which is exactly the reminder wanted, so
        // that "outstanding" cannot quietly become "forgotten".
        var missing = new[] { "big-bank-plc.dsl", "aws-deployment.dsl" }
            .Where(name => !File.Exists(IoPath.Combine("Fixtures", name)))
            .ToArray();
        if (missing.Length == 0)
        {
            Assert.Fail("The real-world fixtures have arrived: update Fixtures/readme.md's table and delete this test.");
        }

        var readme = Bytes("readme.md");
        Assert.All(missing, name => Assert.Contains(name, readme, StringComparison.Ordinal));
        Assert.Contains("outstanding", readme, StringComparison.OrdinalIgnoreCase);
    }
}
