using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>"Arrange diagram" on a hype cycle graph: only rows change, and no two elements share a row where they meet.</summary>
public class GhgArrangementTests
{
    public static TheoryData<string> Examples =>
    [
        "coal-technologies", "digital-trends", "electric-vehicles", "energy-breakthroughs", "eras-of-innovation",
        "internet-evolution", "llms-and-agents", "technology-trends", "warfare-in-ukraine",
    ];

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryExample_ArrangesWithoutOverlap_InNoMoreRowsThanItHad(string example)
    {
        // Arrange.
        var model = GhgParser.Parse(LineDocument.Parse(File.ReadAllText(GhgModuleFiles.ExampleNamed(example))));
        var trends = model.Trends.Where(trend => trend.HasSpan).ToList();

        // Act.
        var rows = GhgArrangement.RowsOf(model);

        // Assert: two trends on one row are apart by at least the later one's name, which is drawn before it.
        foreach (var a in trends)
        {
            foreach (var b in trends.Where(b => string.CompareOrdinal(a.Id, b.Id) < 0 && rows[a.Id] == rows[b.Id]))
            {
                (var first, var second) = a.Start <= b.Start ? (a, b) : (b, a);
                var clear = GhgScale.XOf(second.Start!.Value, model.TimeUnit) - GhgScale.XOf(first.Stop!.Value, model.TimeUnit);
                Assert.True(clear >= TextMetric.WidthOf(second.Name, 12), $"{example}: {first.Name} and {second.Name} meet on row {rows[a.Id]}.");
            }
        }

        var before = model.Trends.Select(trend => trend.Row).Concat(model.Triggers.Select(trigger => trigger.Row)).Max() + 1;
        Assert.True(rows.Values.Max() + 1 <= before, $"{example}: {rows.Values.Max() + 1} rows, where it had {before}.");
    }

    [Fact]
    public void InfluencedTrends_AreArrangedOnRowsCloseTogether()
    {
        // Arrange: a far-off trend that influences one at the bottom of a stack of three.
        var model = GhgParser.Parse(LineDocument.Parse("""
            gartner-hypecycle-graph: 1
            trends:
              - id: a
                name: A
                start: 2000-01
                stop: 2004-01
                row: 0
              - id: b
                name: B
                start: 2000-01
                stop: 2004-01
                row: 1
              - id: c
                name: C
                start: 2000-01
                stop: 2004-01
                row: 2
              - id: later
                name: Later
                start: 2001-01
                stop: 2006-01
                row: 7
            influences:
              - id: a--later
                from: a
                to: later
            """));

        // Act.
        var rows = GhgArrangement.RowsOf(model);

        // Assert.
        Assert.Equal(1, Math.Abs(rows["a"] - rows["later"]));
    }
}
