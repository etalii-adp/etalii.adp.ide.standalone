using EtAlii.Adp.Common;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using Xunit;
using YamlDotNet.Core;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.DependencyGraph.Tests;

/// <summary>
/// Reading a document into the model everything else is a function over.
/// </summary>
/// <remarks>
/// The line ranges get more attention here than the values do, because a wrong value is visible
/// on the canvas the moment anybody looks, while a wrong range is invisible until an edit
/// rewrites the wrong lines - and by then it has already damaged somebody's file.
/// </remarks>
public class DependencyGraphParserTests
{
    private static string FixturesFolder => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static DependencyGraphModel ParseFixture(string fixture) =>
        DependencyGraphParser.Parse(LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(FixturesFolder, fixture))));

    private static DependencyGraphModel Parse(string yaml) =>
        DependencyGraphParser.Parse(LineDocument.Parse(yaml));

    public static TheoryData<string> EveryFixture()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(FixturesFolder, "*.dgr"))
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
        var model = DependencyGraphParser.Parse(document);

        // Each declaration is checked against a second thing it must cover besides its id. An id
        // alone is far too weak a property: a range that has collapsed to the single `- id: x`
        // line still contains the id and still overlaps nothing, so a test asserting only that
        // passes against exactly the bug it was written to catch.
        //
        // The second thing is the `row:` line rather than the label, because a label read back
        // out of the model has lost its quoting - `apostrophe''s` in the file is `apostrophe's`
        // in the model, and asserting the raw text contains the cooked value fails on exactly
        // the fixture that exists to prove quoting survives.
        var ranges = model.Elements
            .Select(element => (element.Id, element.Range, AlsoCovers: $"row: {element.Row}"))
            .Concat(model.Relations.Select(relation => (relation.Id, relation.Range, AlsoCovers: relation.To)))
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
        var model = ParseFixture("simple.dgr");

        // Assert.
        Assert.Equal(2, model.Elements.Count);
        Assert.Equal("k7Qv2mXa", model.Elements[0].Id);
        Assert.Equal("API gateway", model.Elements[0].Label);
        Assert.Equal(240d, model.Elements[0].X);
        Assert.Equal(0, model.Elements[0].Row);

        Assert.Equal("b3Rt9wYz", model.Elements[1].Id);
        Assert.Equal(480d, model.Elements[1].X);
        Assert.Equal(1, model.Elements[1].Row);

        var relation = Assert.Single(model.Relations);
        Assert.Equal("k7Qv2mXa", relation.From);
        Assert.Equal("b3Rt9wYz", relation.To);
        Assert.Equal("verifies tokens with", relation.Label);
    }

    [Fact]
    public void TheRelationsDirectionIsInTheDocument_NotADrawingConvention()
    {
        // Act.
        // The one thing this type adds rather than deletes: `from` depends on `to`, and which
        // way round that is has to survive the parse or the arrowhead points at the wrong box.
        var model = ParseFixture("relations.dgr");

        // Assert.
        var outbound = model.Relations.Where(relation => relation.From == "src00001").ToList();
        var inbound = model.Relations.Where(relation => relation.From == "dst00001").ToList();
        Assert.Equal(2, outbound.Count);
        Assert.All(outbound, relation => Assert.Equal("dst00001", relation.To));
        var single = Assert.Single(inbound);
        Assert.Equal("src00001", single.To);
        Assert.Equal("", single.Label);
    }

    [Fact]
    public void CoordinatesAreReadAsWritten_WholeFractionalNegativeAndZero()
    {
        // Arrange & act.
        // x is a plain canvas number, never a time and never derived from one.
        var model = ParseFixture("coordinates.dgr");

        // Assert.
        Assert.Equal(320d, model.Elements.Single(element => element.Id == "whole001").X);
        Assert.Equal(412.5d, model.Elements.Single(element => element.Id == "fract001").X);
        Assert.Equal(-180d, model.Elements.Single(element => element.Id == "negat001").X);
        Assert.Equal(0d, model.Elements.Single(element => element.Id == "zeroo001").X);
    }

    [Fact]
    public void ACoordinateIsReadInvariantly_WhateverTheMachinesCulture()
    {
        // Arrange.
        // A machine whose decimal separator is a comma must read the same document the same way,
        // or the graph is laid out differently in different countries.
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("nl-NL");
        try
        {
            // Act.
            var model = Parse("dependencies: 1\nelements:\n  - id: a\n    x: 412.5\n    row: 0\n");

            // Assert.
            Assert.Equal(412.5d, model.Elements[0].X);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previous;
        }
    }

    [Fact]
    public void RowsAreReadAsWritten_IncludingOutOfOrderAndNonContiguous()
    {
        // Act.
        var model = ParseFixture("shared-rows.dgr");

        // Assert.
        // Two nodes share row 0, and nothing between rows 3 and 12 exists. A row is a placement
        // grid, so none of that is the module's business to normalise.
        Assert.Equal([3, 0, 0, 12], model.Elements.Select(element => element.Row));
    }

    [Fact]
    public void UnmodelledKeys_DoNotDisturbTheParse()
    {
        // Act.
        var model = ParseFixture("unmodelled-keys.dgr");

        // Assert.
        Assert.Equal(2, model.Elements.Count);
        Assert.Equal("Carries unknown keys", model.Elements[0].Label);
        Assert.Single(model.Relations);
    }

    [Fact]
    public void UnusualIndentation_ParsesWithHonestRanges()
    {
        // Arrange & act.
        var model = ParseFixture("indentation.dgr");

        // Assert.
        Assert.Equal(2, model.Elements.Count);
        Assert.True(model.Elements[0].Range.Length >= 4, "an element's range should cover the keys it declares");
    }

    [Fact]
    public void QuotingIsTheAuthorsBusiness_AndTheValueComesThroughUnquoted()
    {
        // Act.
        var model = ParseFixture("scalars.dgr");

        // Assert.
        Assert.Equal("Quoted: because it contains a colon", model.Elements[0].Label);
        Assert.Contains("apostrophe's worth", model.Elements[1].Label, StringComparison.Ordinal);
        Assert.Contains("\"escaped\" quotes", model.Elements[3].Label, StringComparison.Ordinal);
        Assert.Contains("naïve café", model.Elements[4].Label, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnreadableCoordinate_IsZeroRatherThanACrash()
    {
        // Arrange & act.
        // The rest of the diagram still draws and the rules report the problem, so the parser
        // cannot be the thing that gives up.
        var model = Parse("dependencies: 1\nelements:\n  - id: a\n    label: Broken\n    x: sideways\n    row: 0\n");

        // Assert.
        var element = Assert.Single(model.Elements);
        Assert.Equal(0d, element.X);
        Assert.Equal("Broken", element.Label);
    }

    [Fact]
    public void AMissingCoordinate_IsZero()
    {
        // Arrange & act.
        // A hand-written node with no x is at the origin, not absent from the canvas.
        var model = Parse("dependencies: 1\nelements:\n  - id: a\n    label: No x\n    row: 2\n");

        // Assert.
        Assert.Equal(0d, model.Elements[0].X);
        Assert.Equal(2, model.Elements[0].Row);
    }

    [Fact]
    public void AnUnreadableRow_IsRowZeroRatherThanACrash()
    {
        // Arrange & act.
        var model = Parse("dependencies: 1\nelements:\n  - id: a\n    x: 10\n    row: sideways\n");

        // Assert.
        Assert.Equal(0, model.Elements[0].Row);
    }

    [Fact]
    public void ANegativeRow_IsAsValidAsAPositiveOne()
    {
        // Arrange & act.
        var model = Parse("dependencies: 1\nelements:\n  - id: a\n    x: 10\n    row: -4\n");

        // Assert.
        Assert.Equal(-4, model.Elements[0].Row);
    }

    [Fact]
    public void AnEmptyDocument_IsAnEmptyModel()
    {
        // Act & assert.
        Assert.Empty(Parse("").Elements);
        Assert.Empty(Parse("dependencies: 1\n").Elements);
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
}
