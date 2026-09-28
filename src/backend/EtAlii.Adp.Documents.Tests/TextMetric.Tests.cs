using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The text width metric, checked against the golden fixture both tiers read
/// (backend-centralization R10.1, R10.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fixture is the specification; these tests only apply it.</b> A case added to
/// <c>src/fixtures/cross-tier/text-metric.json</c> is checked here without editing this file, and the
/// client suite reads the same file for its fitting function. So a disagreement between the tiers
/// shows up as a red on whichever side drifted, instead of as text that overflows the box the
/// backend sized for it.
/// </para>
/// <para>
/// Padding, minimum and maximum are each module's own (R10.2), so no case here includes them.
/// </para>
/// </remarks>
public class TextMetricTests
{
    private static readonly TextMetricFixture Fixture = Load();

    [Fact]
    public void TheFixtureLoaded_AndCarriesTheCasesTheDesignNames()
    {
        // The case test loops over the fixture, so a fixture that parsed to an empty list would let it
        // pass by checking nothing. This is the test that cannot pass that way.
        Assert.NotEmpty(Fixture.Reason);
        Assert.True(Fixture.Tolerance > 0, "the fixture names no tolerance");

        // The cases the design's cross-tier table names by kind, so none can be dropped unnoticed:
        // an empty string, one character, a long string and a string with spaces, at two font sizes.
        // ReSharper disable ParameterOnlyUsedForPreconditionCheck.Local - Reason: Used in a test case which is acceptable.
        Assert.Contains(Fixture.Cases, @case => @case.Text.Length == 0);
        Assert.Contains(Fixture.Cases, @case => @case.Text.Length == 1);
        Assert.Contains(Fixture.Cases, @case => @case.Text.Length > 50);
        Assert.Contains(Fixture.Cases, @case => @case.Text.Contains(' ', StringComparison.Ordinal));
        Assert.True(Fixture.Cases.Select(@case => @case.FontSize).Distinct().Count() >= 2, "the fixture has fewer than two font sizes");
        // ReSharper restore ParameterOnlyUsedForPreconditionCheck.Local
    }

    [Fact]
    public void TheSharedValues_AreTheOnesTheFixtureStates()
    {
        // The client reads these two numbers from the fixture rather than from this class, so a change
        // here that the fixture does not carry would leave the tiers measuring differently.
        Assert.Equal(TextMetric.AverageAdvance, Fixture.AverageAdvance);
        Assert.Equal(TextMetric.DefaultFontSize, Fixture.DefaultFontSize);
    }

    [Fact]
    public void EveryCase_MeasuresToItsWidth()
    {
        var failures = new List<string>();
        foreach (var @case in Fixture.Cases)
        {
            var width = TextMetric.WidthOf(@case.Text, @case.FontSize);
            if (Math.Abs(width - @case.Width) > Fixture.Tolerance)
            {
                failures.Add($"\"{@case.Text}\" at {@case.FontSize}: measured {width}, expected {@case.Width} ({@case.Why})");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void TheDefaultFontSize_IsTheOneTheFixtureMeasuresAt()
    {
        var failures = Fixture.Cases
            .Where(@case => Math.Abs(@case.FontSize - Fixture.DefaultFontSize) < double.Tolerance)
            .Where(@case => Math.Abs(TextMetric.WidthOf(@case.Text) - @case.Width) > Fixture.Tolerance)
            .Select(@case => $"\"{@case.Text}\": measured {TextMetric.WidthOf(@case.Text)}, expected {@case.Width}")
            .ToList();

        Assert.Empty(failures);
    }

    [Fact]
    public void ANullText_IsRefused() =>
        Assert.Throws<ArgumentNullException>(() => TextMetric.WidthOf(null!));

    private static TextMetricFixture Load()
    {
        var path = IoPath.Combine(LocateRepositoryRoot(), "src", "fixtures", "cross-tier", "text-metric.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<TextMetricFixture>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"The fixture at {path} is empty.");
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

        throw new InvalidOperationException("The repository root was not found above the test assembly.");
    }

    private sealed record TextMetricFixture(string Reason, double AverageAdvance, double DefaultFontSize, double Tolerance, IReadOnlyList<TextCase> Cases);

    private sealed record TextCase(string Text, double FontSize, double Width, string Why);
}
