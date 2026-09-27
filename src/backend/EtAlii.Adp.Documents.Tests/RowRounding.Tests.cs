using System.Globalization;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The row rounding rule, checked against the golden fixture both tiers read
/// (backend-centralization R9.1, R9.2).
/// </summary>
/// <remarks>
/// <b>The fixture is the specification; these tests only apply it.</b> A case added to
/// <c>src/fixtures/cross-tier/row-rounding.json</c> is checked here without editing this file, and the
/// client suite reads the same file for its <c>snapToStep</c>. So a disagreement between the tiers shows
/// up as a red on whichever side drifted, instead of as a drop that lands a row away from its preview.
/// </remarks>
public class RowRoundingTests
{
    private static readonly RowRoundingFixture Fixture = Load();

    [Fact]
    public void TheFixtureLoaded_AndCarriesTheCasesTheDesignNames()
    {
        // The rounding test loops over the fixture, so a fixture that parsed to an empty list would let
        // it pass by checking nothing. This is the test that cannot pass that way.
        Assert.NotEmpty(Fixture.Reason);
        Assert.NotEmpty(Fixture.Cases);

        // The cases the design's cross-tier table names by kind, so none can be dropped unnoticed:
        // exact halves either side of zero, negative zero, a negative row, and a value between lines.
        var inRows = Fixture.Cases.Select(@case => @case.Y / @case.RowHeight).ToList();
        Assert.Contains(inRows, rows => rows > 0 && rows % 1 == 0.5);
        Assert.Contains(inRows, rows => rows < 0 && rows % 1 == -0.5);
        Assert.Contains(Fixture.Cases, @case => @case.Y == 0 && double.IsNegative(@case.Y));
        Assert.Contains(Fixture.Cases, @case => @case.Row < 0);
        Assert.Contains(inRows, rows => Math.Abs(rows % 1) is > 0 and not 0.5);
    }

    [Fact]
    public void EveryPosition_RoundsToTheFixturesRow()
    {
        var failures = Fixture.Cases
            .Select(@case => (@case, actual: RowRounding.ToNearestRow(@case.Y, @case.RowHeight)))
            .Where(result => result.actual != result.@case.Row)
            .Select(result => string.Create(CultureInfo.InvariantCulture,
                $"y {result.@case.Y:R} at row height {result.@case.RowHeight:R}: row {result.actual}, expected {result.@case.Row} ({result.@case.Why})"))
            .ToList();

        Assert.Empty(failures);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-60d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ARowHeightThatIsNotPositiveAndFinite_IsRefused(double rowHeight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RowRounding.ToNearestRow(30d, rowHeight));
    }

    private static RowRoundingFixture Load()
    {
        var path = IoPath.Combine(LocateRepositoryRoot(), "src", "fixtures", "cross-tier", "row-rounding.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<RowRoundingFixture>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"{path} deserialised to nothing.");
    }

    private static string LocateRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "docs")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/diagrams beside docs) was not found above the test binary.");
    }

    private sealed record RowRoundingFixture(string Reason, IReadOnlyList<RowCase> Cases);

    private sealed record RowCase(double Y, double RowHeight, int Row, string Why);
}
