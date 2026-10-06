using System.Globalization;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests.Parity;

/// <summary>
/// The DISL model built from the binding's reading (<see cref="DislModelBuilder.From"/>, through the
/// definition's <c>persistence.typeMap</c>) holds what <see cref="GhgParser.Parse(GhgBody)"/>
/// reads, for every document of the corpus: the same trends, triggers, notes and influences, in the
/// same order, with the same ids and the same values - a value the parser falls back on read as the
/// attribute's default, one it passes over left unset.
/// </summary>
public class GhgDislModelTests
{
    public static TheoryData<string> Documents() => [.. GhgDisl.Texts().Select(document => document.Path)];

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheModel_HoldsWhatGhgParserReads(string path)
    {
        // Arrange.
        var text = GhgDisl.Texts().Single(document => document.Path == path).Text;
        var body = GhgBody.Parse(text);
        var expected = GhgParser.Parse(body);

        // Act.
        var model = DislModelBuilder.From(body.Model, GhgDisl.Specification);

        // Assert.
        var diagram = model.Diagram;
        Assert.Equal(body.Model.Unreadable, model.IsUnreadable);
        Assert.Equal(expected.Trends.Select(Line), diagram.NodesOfType("Trend").Select(TrendLine));
        Assert.Equal(expected.Triggers.Select(Line), diagram.NodesOfType("Trigger").Select(TriggerLine));
        Assert.Equal(expected.Notes.Select(Line), diagram.NodesOfType("Note").Select(NoteLine));
        Assert.Equal(expected.Influences.Select(Line), diagram.RelationsOfType("Influence").Select(InfluenceLine));
        Assert.Equal(expected.Trends.Count + expected.Triggers.Count + expected.Notes.Count, diagram.Nodes.Count);
        Assert.Equal(expected.Influences.Count, diagram.Relations.Count);
        Assert.Equal(expected.Unit?.Name ?? "month", GhgTimeUnit.Named(diagram.ValueOf("unit") as string)?.Name ?? "month");
        Assert.Equal(expected.Problems.Count(problem => problem.Message.EndsWith("is not a mapping and was passed over.", StringComparison.Ordinal)), model.Unreadable.Count);
        Assert.Equal(expected.Version?.ToString(CultureInfo.InvariantCulture), model.Header.SingleOrDefault()?.Attributes.GetValueOrDefault("version")?.ToString());
    }

    [Fact]
    public void TheCorpus_ReachesEveryKindOfEntry()
    {
        var models = GhgDisl.Texts().Select(document => DislModelBuilder.From(GhgBody.Parse(document.Text).Model, GhgDisl.Specification).Diagram).ToList();
        Assert.True(models.Sum(diagram => diagram.NodesOfType("Trend").Count) > 500);
        Assert.True(models.Sum(diagram => diagram.NodesOfType("Trigger").Count) > 100);
        Assert.True(models.Sum(diagram => diagram.NodesOfType("Note").Count) > 5);
        Assert.True(models.Sum(diagram => diagram.Relations.Count) > 1000);
        Assert.Contains(models, diagram => diagram.Attributes.ContainsKey("unit"));
    }

    /// <summary>Text the reader writes as a month: read as the month index, or, not in the <c>±YYYY-MM</c> form, left unset as the parser leaves it.</summary>
    [Theory]
    [InlineData("1900-01", 1900L * 12)]
    [InlineData("-3200-01", -3200L * 12)]
    [InlineData("-0001-12", -1L)]
    [InlineData("2026-00", null)]
    [InlineData("2026-13", null)]
    [InlineData("2026-1", null)]
    [InlineData("soon", null)]
    public void AMonth_IsReadAsAMonthIndexOrLeftUnset(string written, long? expected)
    {
        var text = $"gartner-hypecycle-graph: 1\r\ntrends:\r\n  - id: a\r\n    name: A\r\n    start: \"{written}\"\r\n    stop: 2000-01\r\n    phases: 4\r\ninfluences: []\r\n";
        var trend = Assert.Single(DislModelBuilder.From(GhgBody.Parse(text).Model, GhgDisl.Specification).Diagram.Nodes);

        Assert.Equal(expected, trend.Attributes.TryGetValue("start", out var start) ? (long?)start : null);
        Assert.Equal(expected.HasValue, GhgParser.Parse(text).Trends[0].Start.HasValue);
    }

    // ---- one line per entry, as the parser and the model each have it ---------------------------

    private static string Line(GhgTrend trend) =>
        $"{trend.Id} | {trend.Name} | {trend.Start} {trend.Stop} | row {trend.Row} | phases {trend.Phases} | ends {string.Join(",", trend.DraggedEnds)} | tags {string.Join(",", trend.Tags)} | {trend.Description}";

    private static string TrendLine(DislElement trend) =>
        $"{Id(trend)} | {trend.ValueOf("name")} | {Stored(trend, "start")} {Stored(trend, "stop")} | row {trend.ValueOf("row")} | phases {trend.ValueOf("phases")} | ends {Stored(trend, "peakEnd")},{Stored(trend, "troughEnd")},{Stored(trend, "slopeEnd")} | tags {Tags(trend)} | {trend.ValueOf("description")}";

    private static string Line(GhgTrigger trigger) =>
        $"{trigger.Id} | {trigger.Name} | {trigger.Date} | row {trigger.Row} | tags {string.Join(",", trigger.Tags)} | {trigger.Description}";

    private static string TriggerLine(DislElement trigger) =>
        $"{Id(trigger)} | {trigger.ValueOf("name")} | {Stored(trigger, "date")} | row {trigger.ValueOf("row")} | tags {Tags(trigger)} | {trigger.ValueOf("description")}";

    private static string Line(GhgNote note) =>
        $"{note.Id} | {note.Text} | {note.At} | row {note.Row} | {Number(note.Width)} x {Number(note.Height)}";

    private static string NoteLine(DislElement note) =>
        $"{Id(note)} | {note.ValueOf("text")} | {Stored(note, "at")} | row {note.ValueOf("row")} | {Number(note.Attributes.GetValueOrDefault("width") as double?)} x {Number(note.Attributes.GetValueOrDefault("height") as double?)}";

    private static string Line(GhgInfluence influence) =>
        $"{influence.Id} | {influence.From} -> {influence.To} | from {End(influence.FromEnd)} | to {End(influence.ToEnd)} | {influence.Description}";

    private static string InfluenceLine(DislElement influence)
    {
        // An influence from a trigger states no from end: the parser reads none, whatever is written.
        var from = influence.Source is { } source && source.IsA("Trigger") ? End(GhgEnd.None) : End(influence, "from");
        return $"{Id(influence)} | {influence.SourceId ?? ""} -> {influence.TargetId ?? ""} | from {from} | to {End(influence, "to")} | {influence.ValueOf("description")}";
    }

    private static string End(GhgEnd end) => $"{end.Phase}/{end.Edge}/{Number(end.At)}";

    private static string End(DislElement influence, string side) =>
        $"{influence.Attributes.GetValueOrDefault(side + "Phase")}/{influence.Attributes.GetValueOrDefault(side + "Edge")}/{Number(influence.Attributes.GetValueOrDefault(side + "At") as double?)}";

    /// <summary>
    /// The id as written. FBL addresses an entry whose id is missing, or repeats an earlier one, by its
    /// place (<c>trend@15</c>); the parser keeps what is written, which the binding also reads as the
    /// host attribute <c>storedId</c>.
    /// </summary>
    private static string Id(DislElement element) =>
        element.IdIsStored ? element.Id : element.HostAttributes.GetValueOrDefault("storedId") as string ?? "";

    private static string Stored(DislElement element, string attribute) =>
        element.Attributes.TryGetValue(attribute, out var value) ? ((long)value!).ToString(CultureInfo.InvariantCulture) : "";

    private static string Tags(DislElement element) => string.Join(",", (IReadOnlyList<object?>)element.ValueOf("tags")!);

    private static string Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
}
