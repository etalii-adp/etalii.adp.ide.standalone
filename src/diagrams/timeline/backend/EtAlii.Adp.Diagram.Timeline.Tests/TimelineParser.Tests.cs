using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// Reading a document into the model everything else is a function over.
/// </summary>
/// <remarks>
/// The line ranges get more attention here than the values do, because a wrong value is visible
/// on the canvas the moment anybody looks, while a wrong range is invisible until an edit
/// rewrites the wrong lines - and by then it has already damaged somebody's file.
/// </remarks>
public class TimelineParserTests
{
    private static string FixturesFolder => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static TimelineModel ParseFixture(string fixture) =>
        TimelineParser.Parse(LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(FixturesFolder, fixture))));

    private static TimelineModel Parse(string yaml) =>
        TimelineParser.Parse(LineDocument.Parse(yaml));

    public static TheoryData<string> EveryFixture()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixturesFolder, "*.tml"))
        {
            data.Add(IoPath.GetFileName(path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void EveryFixture_Parses(string fixture)
    {
        // Act.
        var model = ParseFixture(fixture);

        // Assert.
        Assert.NotEmpty(model.Elements);
    }

    [Theory]
    [MemberData(nameof(EveryFixture))]
    public void EveryDeclarationOwnsItsOwnLines(string fixture)
    {
        // Arrange.
        // The test that catches the end-mark problem. A block collection has no closing token, so
        // an uncorrected range either collapses to one line or runs into the next declaration -
        // and the writer would then edit somebody else's element.
        var text = File.ReadAllText(IoPath.Combine(FixturesFolder, fixture));
        var document = LineDocument.Parse(text);
        var model = TimelineParser.Parse(document);

        // Each declaration is checked against a second thing it must cover besides its id. An id
        // alone is far too weak a property: a range that has collapsed to the single `- id: x`
        // line still contains the id and still overlaps nothing, so a test asserting only that
        // passes against exactly the bug it was written to catch. Verified by mutation - reverting
        // the subtree end-mark correction fails this test.
        var ranges = model.Elements
            .Select(element => (element.Id, element.Range, AlsoCovers: element.Begin.Text))
            .Concat(model.Connections.Select(connection => (connection.Id, connection.Range, AlsoCovers: connection.To)))
            .ToList();

        // Assert.
        foreach (var (id, range, alsoCovers) in ranges)
        {
            Assert.True(range.Start >= 0, $"{id} starts before the document");
            Assert.True(range.End < document.Lines.Count, $"{id} ends past the document");
            Assert.True(range.End >= range.Start, $"{id} has an inverted range");

            var covered = string.Join(
                "\n",
                Enumerable.Range(range.Start, range.Length).Select(i => document.Lines[i].Text));
            Assert.Contains(id, covered, StringComparison.Ordinal);
            Assert.True(
                covered.Contains(alsoCovers, StringComparison.Ordinal),
                $"{id}'s range ({range.Start}-{range.End}) does not reach the rest of its own declaration");
        }

        // No two declarations claim the same line.
        foreach (var (leftId, left, _) in ranges)
        {
            foreach (var (rightId, right, _) in ranges)
            {
                if (ReferenceEquals(leftId, rightId) && left.Equals(right))
                {
                    continue;
                }

                var overlaps = left.Start <= right.End && right.Start <= left.End;
                Assert.False(
                    overlaps && !left.Equals(right),
                    $"{leftId} ({left.Start}-{left.End}) and {rightId} ({right.Start}-{right.End}) share lines");
            }
        }
    }

    [Fact]
    public void TheOrdinaryDocument_ReadsAsWritten()
    {
        // Act.
        var model = ParseFixture("simple.tml");

        // Assert.
        Assert.Equal(2, model.Elements.Count);
        Assert.Equal("k7Qv2mXa", model.Elements[0].Id);
        Assert.Equal("Discovery", model.Elements[0].Label);
        Assert.Equal(0, model.Elements[0].Row);
        Assert.True(model.Elements[0].IsPeriod);

        Assert.Equal("b3Rt9wYz", model.Elements[1].Id);
        Assert.False(model.Elements[1].IsPeriod);
        Assert.Null(model.Elements[1].End);

        var connection = Assert.Single(model.Connections);
        Assert.Equal("k7Qv2mXa", connection.From);
        Assert.Equal("b3Rt9wYz", connection.To);
        Assert.Equal("gates", connection.Label);
    }

    [Fact]
    public void ADateOnlyValue_IsNotPromotedToAMidnightDateTime()
    {
        // Arrange & act.
        // The failure this guards is silent: both render identically, and the difference only
        // surfaces when a drag writes the value back in the other form.
        var model = ParseFixture("mixed-precision.tml");
        var dateOnly = model.Elements.Single(element => element.Id == "dateonly1");
        var dateTime = model.Elements.Single(element => element.Id == "datetime1");

        // Assert.
        Assert.Equal(TimelinePrecision.Date, dateOnly.Begin.Precision);
        Assert.Equal(TimelinePrecision.DateTime, dateTime.Begin.Precision);
        Assert.Equal("2026-03-02", dateOnly.Begin.Text);
        Assert.Equal("2026-03-02T09:30:00", dateTime.Begin.Text);
    }

    [Fact]
    public void RowsAreReadAsWritten_IncludingOutOfOrderAndNonContiguous()
    {
        // Act.
        var model = ParseFixture("shared-rows.tml");

        // Assert.
        // Two elements share row 0, and nothing between rows 3 and 12 exists. A row is a
        // placement grid, so none of that is the module's business to normalise.
        Assert.Equal([3, 0, 0, 12], model.Elements.Select(element => element.Row));
    }

    [Fact]
    public void UnmodelledKeys_DoNotDisturbTheParse()
    {
        // Act.
        var model = ParseFixture("unmodelled-keys.tml");

        // Assert.
        Assert.Equal(2, model.Elements.Count);
        Assert.Equal("Carries unknown keys", model.Elements[0].Label);
        Assert.Single(model.Connections);
    }

    [Fact]
    public void UnusualIndentation_ParsesWithHonestRanges()
    {
        // Arrange & act.
        var model = ParseFixture("indentation.tml");

        // Assert.
        Assert.Equal(2, model.Elements.Count);
        Assert.True(model.Elements[0].Range.Length >= 4, "an element's range should cover the keys it declares");
    }

    [Fact]
    public void QuotingIsTheAuthorsBusiness_AndTheValueComesThroughUnquoted()
    {
        // Act.
        var model = ParseFixture("scalars.tml");

        // Assert.
        Assert.Equal("Quoted: because it contains a colon", model.Elements[0].Label);
        Assert.Contains("apostrophe's worth", model.Elements[1].Label, StringComparison.Ordinal);
        Assert.Contains("\"escaped\" quotes", model.Elements[3].Label, StringComparison.Ordinal);
        Assert.Contains("naïve café", model.Elements[4].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableTime_IsRecordedRatherThanThrown()
    {
        // Arrange & act.
        // Requirement 12.2 wants a warning naming the element while the rest still draws, so the
        // parser cannot be the thing that gives up.
        var model = Parse("timeline: 1\nelements:\n  - id: a\n    label: Broken\n    begin: not-a-date\n    row: 0\n");

        // Assert.
        var element = Assert.Single(model.Elements);
        Assert.False(element.Begin.IsReadable);
        Assert.Equal("not-a-date", element.Begin.Text);
    }

    [Fact]
    public void AnEndKeyPresentButEmpty_IsAPeriodWithABrokenEnd_NotAMoment()
    {
        // Arrange & act.
        // Collapsing these two would make `end:` with nothing after it indistinguishable from no
        // end at all, and the author's intent is plainly different in each case.
        var withEmptyEnd = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-01-01\n    end:\n    row: 0\n");
        var withNoEnd = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-01-01\n    row: 0\n");

        // Assert.
        Assert.NotNull(withEmptyEnd.Elements[0].End);
        Assert.False(withEmptyEnd.Elements[0].End!.IsReadable);
        Assert.Null(withNoEnd.Elements[0].End);
    }

    [Fact]
    public void AnUnreadableRow_IsRowZeroRatherThanACrash()
    {
        // Arrange & act.
        var model = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-01-01\n    row: sideways\n");

        // Assert.
        Assert.Equal(0, model.Elements[0].Row);
    }

    [Fact]
    public void ANegativeRow_IsAsValidAsAPositiveOne()
    {
        // Arrange & act.
        var model = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-01-01\n    row: -4\n");

        // Assert.
        Assert.Equal(-4, model.Elements[0].Row);
    }

    [Fact]
    public void AnEmptyDocument_IsAnEmptyModel()
    {
        // Act & assert.
        Assert.Empty(Parse("").Elements);
        Assert.Empty(Parse("timeline: 1\n").Elements);
    }

    [Fact]
    public void ADocumentThatIsNotYaml_Throws()
    {
        // Act & assert.
        // The one case the parser does give up on, and the validator turns it into a single
        // located problem rather than a pile of consequences.
        //
        // ThrowsAny rather than Throws: YamlDotNet reports a syntax error as a *subclass* of
        // YamlException, and an exact-type assertion here would pass only by accident of which
        // subclass this particular malformed document happens to produce. The validator catches
        // the base type, so that is what this pins.
        var exception = Assert.ThrowsAny<YamlException>(() => Parse("elements: [\n  - id: a\n"));
        Assert.True(exception.Start.Line >= 1, "a problem the user cannot locate is barely a problem");
    }

    [Fact]
    public void TimesAreTakenAtFaceValue()
    {
        // Arrange & act.
        // No timezone conversion: the same document must draw the same diagram on every machine,
        // whatever the local offset happens to be.
        var model = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-06-15T12:00:00\n");

        // Assert.
        var value = model.Elements[0].Begin.Value!.Value;
        Assert.Equal(2026, value.Year);
        Assert.Equal(6, value.Month);
        Assert.Equal(15, value.Day);
        Assert.Equal(12, value.Hour);

        // The offset itself, not just the components. DateTimeStyles.None assumes the *local*
        // offset for a value that carries none, and the components above are identical either
        // way - which is how that bug got past this test the first time. On any machine whose
        // offset is not zero, this line is the one that fails without the AssumeUniversal fix.
        Assert.Equal(TimeSpan.Zero, value.Offset);
    }
}
