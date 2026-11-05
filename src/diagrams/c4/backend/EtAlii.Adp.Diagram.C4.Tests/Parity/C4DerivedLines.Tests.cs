using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// What a view draws, derived from the definition of its kind (<see cref="C4Definitions.Drawing"/>), is what
/// the module computed by hand (c4-disl-fbl Requirements 3.4 and 3.5): for every document of the parity
/// corpus and every view it declares, the members, every line with its id, its ends, its texts and its
/// order, the title and the legend. A difference names the document and the view.
/// </summary>
public class C4DerivedLinesTests
{
    [Fact]
    public async Task EveryViewsDrawing_IsTheHandWrittenOne()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var differences = new List<string>();

        // Act.
        foreach (var reading in readings)
        {
            foreach ((C4View view, _) in reading.Views)
            {
                var drawing = C4Definitions.Drawing(reading.Workspace, reading.File, view);
                Compare("members", C4RuleSet.MembersOf(reading.Workspace, view).Select(element => element.Id), drawing.Members.Select(element => element.Id));
                Compare("lines", HandWrittenC4.Lines(reading.Workspace, view).Select(Text), drawing.Lines.Select(Text));
                Compare("title", [HandWrittenC4.Title(reading.Workspace, view)], [drawing.Title]);
                Compare("legend", HandWrittenC4.Legend(reading.Workspace, view).Select(Text), drawing.Legend.Select(Text));

                void Compare(string what, IEnumerable<string> expected, IEnumerable<string> actual)
                {
                    List<string> want = [.. expected];
                    List<string> got = [.. actual];
                    if (!want.SequenceEqual(got, StringComparer.Ordinal))
                    {
                        differences.Add($"{reading.Name}, view {view.Key}, {what}:\n  expected {string.Join("\n           ", want)}\n  actual   {string.Join("\n           ", got)}");
                    }
                }
            }
        }

        // Assert.
        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(20)));
    }

    [Fact]
    public async Task NoLine_FailsToBeDerived_OverTheWholeCorpus()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        string[] codes = ["std.derivedFailed", "std.derivedEnds", "std.derivedId"];

        // Act.
        var failures = readings
            .SelectMany(reading => reading.Views.Select(each => (reading, each.View)))
            .SelectMany(pair => C4Body.Of(pair.reading.Workspace, pair.View, C4Definitions.For(pair.View.Kind).Specification, pair.reading.File).Findings
                .Select(finding => $"{pair.reading.Name}, view {pair.View.Key}: {finding.Code} {finding.Message}"))
            .Where(line => codes.Any(code => line.Contains(code, StringComparison.Ordinal)))
            .ToList();

        // Assert.
        Assert.True(failures.Count == 0, string.Join("\n", failures.Take(20)));
    }

    [Fact]
    public async Task AStoredAndADerivedLine_NeverShareAnId()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);

        // Act.
        var shared = new List<string>();
        foreach (var reading in readings)
        {
            foreach ((C4View view, _) in reading.Views)
            {
                var specification = C4Definitions.For(view.Kind).Specification;
                var relations = C4Body.Of(reading.Workspace, view, specification, reading.File).Diagram.Relations;
                var stored = relations.Where(relation => !relation.IsDerived).Select(relation => relation.Id).ToHashSet(specification.IdComparer);
                shared.AddRange(relations.Where(relation => relation.IsDerived && stored.Contains(relation.Id)).Select(relation => $"{reading.Name}, view {view.Key}: {relation.Type.Name} {relation.Id}"));
            }
        }

        // Assert.
        Assert.True(shared.Count == 0, string.Join("\n", shared.Take(20)));
    }

    [Fact]
    public async Task ADerivedLine_GetsNoMenuAndNoRows()
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var offered = new List<string>();
        var derivedLines = 0;

        // Act.
        foreach (var reading in readings)
        {
            var stored = reading.Workspace.Relationships.Select(relationship => relationship.Id).ToHashSet(StringComparer.Ordinal);
            foreach ((C4View view, _) in reading.Views)
            {
                var origin = C4Corpus.OriginOf(view.Kind);
                foreach (var line in C4Definitions.Drawing(reading.Workspace, reading.File, view).Lines.Where(line => !stored.Contains(line.Id)))
                {
                    derivedLines++;
                    if (C4Definitions.Menus(reading.Workspace, reading.File, view, origin, line.Id).Count > 0 || C4Definitions.Rows(reading.Workspace, reading.File, view, origin, line.Id).Count > 0)
                    {
                        offered.Add($"{reading.Name}, view {view.Key}: {line.Id}");
                    }
                }
            }
        }

        // Assert.
        // Lifted relationships, lines between instances and numbered interactions all occur in the corpus.
        Assert.True(derivedLines > 0);
        Assert.True(offered.Count == 0, string.Join("\n", offered.Take(20)));
    }

    [Theory]
    [InlineData("big-bank-plc.dsl", "Containers")]
    [InlineData("big-bank-plc.dsl", "LiveDeployment")]
    [InlineData("big-bank-plc.dsl", "SignIn")]
    public async Task EachKindOfDerivedLine_OccursInTheCorpus(string document, string viewKey)
    {
        // Arrange.
        var readings = await C4Corpus.ReadingsAsync(TestContext.Current.CancellationToken);
        var reading = readings.First(each => each.Name.EndsWith("/" + document, StringComparison.Ordinal));
        var view = reading.Workspace.FindView(viewKey)!;
        var stored = reading.Workspace.Relationships.Select(relationship => relationship.Id).ToHashSet(StringComparer.Ordinal);

        // Act.
        var derived = C4Definitions.Drawing(reading.Workspace, reading.File, view).Lines.Where(line => !stored.Contains(line.Id)).ToList();

        // Assert.
        Assert.NotEmpty(derived);
    }

    private static string Text(C4DrawnLine line) =>
        $"{line.Id} | {line.SourceId} -> {line.DestinationId} | {TranscriptText.Quoted(line.Description)} | {TranscriptText.Quoted(line.Technology)} | {line.Order}";

    private static string Text(C4LegendItem entry) => $"{entry.Label} | {entry.First.Id}";
}
