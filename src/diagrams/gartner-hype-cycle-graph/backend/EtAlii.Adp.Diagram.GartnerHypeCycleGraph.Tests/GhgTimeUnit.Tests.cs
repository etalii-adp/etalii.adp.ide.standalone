using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// A document's <c>unit:</c> - month, year, decade or century - sets the step its axis is drawn and
/// snapped in, so thousands of years fit on a canvas. The dates stay months whatever the unit.
/// </summary>
public sealed class GhgTimeUnitTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.GhgTimeUnitTests", Guid.NewGuid().ToString("N"));
    private readonly GhgDocumentStore _store = new();

    public GhgTimeUnitTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }

    private static int M(int year, int month = 1) => GhgScale.MonthIndex(year, month);

    private static string Document(string? unit) =>
        "gartner-hypecycle-graph: 1\n"
        + (unit is null ? "" : $"unit: {unit}\n")
        + "trends:\n"
        + "  - id: writing\n    name: Writing\n    start: -3400-01\n    stop: -0500-01\n    row: 0\n    phases: 4\n"
        + "  - id: printing\n    name: Printing\n    start: 1440-01\n    stop: 1700-01\n    row: 2\n    phases: 4\n"
        + "influences:\n";

    private static GhgModel Parse(string text) => GhgParser.Parse(GhgBody.Parse(text));

    [Theory]
    [InlineData("month", 1)]
    [InlineData("year", 12)]
    [InlineData("decade", 120)]
    [InlineData("century", 1200)]
    public void ANamedUnit_IsRead_WithoutAProblem(string name, int months)
    {
        var model = Parse(Document(name));

        Assert.Empty(model.Problems);
        Assert.Equal(name, model.TimeUnit.Name);
        Assert.Equal(months, model.TimeUnit.Months);
    }

    [Fact]
    public void ADocumentThatNamesNoUnit_IsDrawnInMonths()
    {
        var model = Parse(Document(null));

        Assert.Empty(model.Problems);
        Assert.Same(GhgTimeUnit.Month, model.TimeUnit);
    }

    [Theory]
    [InlineData("millennium")]
    [InlineData("Year")]
    [InlineData("[year]")]
    public void AnUnknownUnit_IsReportedOnItsLine_AndDrawnInMonths(string name)
    {
        var model = Parse(Document(name));

        var problem = Assert.Single(model.Problems);
        Assert.Equal(1, problem.Line);
        Assert.Contains("month, year, decade, century", problem.Message, StringComparison.Ordinal);
        Assert.Same(GhgTimeUnit.Month, model.TimeUnit);
        Assert.Equal(2, model.Trends.Count);
    }

    /// <summary>A step of any unit is four canvas units wide, measured from 1900-01.</summary>
    [Theory]
    [InlineData("month", 1901, 0, 48)]
    [InlineData("year", 1901, 0, 4)]
    [InlineData("year", 1800, 0, -400)]
    [InlineData("decade", 2000, 0, 40)]
    [InlineData("decade", -3400, 0, -2120)]
    [InlineData("century", 1500, 0, -16)]
    [InlineData("year", 1900, 6, 2)]
    public void TheXOfAMonth_IsFourUnitsAStep(string name, int year, int months, double x)
    {
        var unit = GhgTimeUnit.Named(name)!;

        Assert.Equal(x, GhgScale.XOf(M(year) + months, unit));
    }

    /// <summary>A snap lands on the start of the nearest step: a January in a diagram of years, a year ending in 0 in one of decades.</summary>
    [Theory]
    [InlineData("year", 401, 2000)]
    [InlineData("year", -397, 1801)]
    [InlineData("decade", 41, 2000)]
    [InlineData("decade", -2119, -3400)]
    [InlineData("century", -17, 1500)]
    public void ASnap_LandsOnTheStartOfTheNearestStep(string name, double x, int year)
    {
        var unit = GhgTimeUnit.Named(name)!;

        Assert.Equal(M(year), GhgScale.NearestMonthAt(x, unit));
    }

    [Theory]
    [InlineData("year", 7.9, 1901)]
    [InlineData("year", -0.1, 1899)]
    [InlineData("decade", 43.9, 2000)]
    public void ADrop_IsTheStartOfTheStepItFallsIn(string name, double x, int year)
    {
        var unit = GhgTimeUnit.Named(name)!;

        Assert.Equal(M(year), GhgScale.MonthContaining(x, unit));
    }

    /// <summary>The canvas is handed each trend at the unit's scale, and the unit itself on every trend's payload.</summary>
    [Fact]
    public void TheMapper_DrawsEveryTrendAtItsDocumentsScale_AndNamesTheUnit()
    {
        var elements = new GhgElementMapper().Visible(Parse(Document("decade")), DiagramViewport.Unbounded);

        var writing = elements.Single(element => element.Id == "writing");
        var payload = GhgTrendPayload.Parser.ParseFrom(writing.Payload.ToArray());
        Assert.Equal("decade", payload.Unit);
        Assert.Equal((2900 * 12) * 4 / 120.0, payload.Width);
        Assert.Equal(GhgScale.XOf(M(-3400), GhgTimeUnit.Decade) + (payload.Width / 2), writing.X);
        Assert.All(elements, element => Assert.Equal("decade", GhgTrendPayload.Parser.ParseFrom(element.Payload.ToArray()).Unit));
    }

    /// <summary>A trend moved in a diagram of decades starts on a decade, and keeps its length.</summary>
    [Fact]
    public async Task AMove_InADiagramOfDecades_LandsOnADecade()
    {
        var body = Path.Combine(_folder, "eras.ghg");
        await File.WriteAllTextAsync(body, Document("decade"), TestContext.Current.CancellationToken);

        var moved = await new GhgTestDispatcher(_store).DispatchAsync(
            new SetGhgPlacementCommand(body, "printing", GhgScale.XOf(M(1453, 7), GhgTimeUnit.Decade), GhgScale.TopOf(4)),
            TestContext.Current.CancellationToken);

        Assert.True(moved.IsSuccess, moved.Error);
        var printing = Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).Trends.Single(trend => trend.Id == "printing");
        Assert.Equal((M(1450), M(1710), 4), (printing.Start!.Value, printing.Stop!.Value, printing.Row));
    }

    /// <summary>A trend dropped into a diagram of centuries is twelve centuries long, from the century it landed in.</summary>
    [Fact]
    public async Task ADrop_InADiagramOfCenturies_IsTwelveCenturiesLong()
    {
        var body = Path.Combine(_folder, "eras.ghg");
        await File.WriteAllTextAsync(body, Document("century"), TestContext.Current.CancellationToken);

        var added = await new GhgTestDispatcher(_store).DispatchAsync(
            new AddGhgTrendCommand(body, GhgScale.XOf(M(1250), GhgTimeUnit.Century), GhgScale.TopOf(6) + 10),
            TestContext.Current.CancellationToken);

        Assert.True(added.IsSuccess, added.Error);
        var trend = Parse(await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken)).Trends.Single(trend => trend.Name == AddGhgTrendCommandHandler.DefaultName);
        Assert.Equal((M(1200), M(2400)), (trend.Start!.Value, trend.Stop!.Value));
    }
}
