using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Documents.Tests;

/// <summary>
/// The gesture id grammar, checked against the golden fixture both tiers read
/// (backend-centralization R11.1, R11.3).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fixture is the specification; these tests only apply it.</b> A case added to
/// <c>src/fixtures/cross-tier/gesture-ids.json</c> is checked here without editing this file, and the
/// client suite reads the same file for its builders. So a disagreement between the tiers shows up as
/// a red on whichever side drifted, instead of as a gesture that works on one side only.
/// </para>
/// <para>
/// Each test collects every failing case before asserting, so a single run names every broken id
/// instead of stopping at the first.
/// </para>
/// </remarks>
public class GestureIdsTests
{
    private static readonly GestureFixture Fixture = Load();

    [Fact]
    public void TheFixtureLoaded_AndCarriesTheCasesTheDesignNames()
    {
        // Every other test loops over the fixture, so a fixture that parsed to empty lists would let
        // them all pass by checking nothing. This is the test that cannot pass that way.
        Assert.NotEmpty(Fixture.Reason);
        Assert.NotEmpty(Fixture.Placements.Xy.Valid);
        Assert.NotEmpty(Fixture.Placements.Xy.Invalid);
        Assert.NotEmpty(Fixture.Placements.Row.Valid);
        Assert.NotEmpty(Fixture.Placements.Row.Invalid);
        Assert.NotEmpty(Fixture.Relations.Valid);
        Assert.NotEmpty(Fixture.Relations.Invalid);

        // The three invalid shapes the design's cross-tier table names by kind, so none can be dropped
        // from the fixture unnoticed: an empty end, a missing arrow, and a trailing separator.
        var invalid = Fixture.Relations.Invalid.Concat(Fixture.Placements.Xy.Invalid).Select(@case => @case.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("rel:a->", invalid);
        Assert.Contains("rel:->b", invalid);
        Assert.Contains("rel:ab", invalid);
        Assert.Contains("new:1,2,", invalid);
    }

    [Fact]
    public void AValidPoint_ParsesToItsCoordinates_AndBuildsBackToTheSameId()
    {
        var failures = new List<string>();
        foreach (var @case in Fixture.Placements.Xy.Valid)
        {
            if (!GestureIds.TryParsePlacement(@case.Id, out var x, out var y) || Math.Abs(x - @case.X) > double.Tolerance || Math.Abs(y - @case.Y) > double.Tolerance)
            {
                failures.Add($"{@case.Id}: parsed as ({x}, {y}), expected ({@case.X}, {@case.Y})");
            }

            var built = GestureIds.Placement(@case.X, @case.Y);
            if (built != @case.Id)
            {
                failures.Add($"{@case.Id}: built back as {built}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void AnInvalidPoint_IsRefused()
    {
        var accepted = Fixture.Placements.Xy.Invalid
            .Where(@case => GestureIds.TryParsePlacement(@case.Id, out _, out _))
            .Select(@case => $"{@case.Id} ({@case.Why})")
            .ToList();

        Assert.Empty(accepted);
    }

    [Fact]
    public void AValidRowPlacement_ParsesToItsPositionAndRow_AndBuildsBackToTheSameId()
    {
        var failures = new List<string>();
        foreach (var @case in Fixture.Placements.Row.Valid)
        {
            if (!GestureIds.TryParseRowPlacement(@case.Id, out var x, out var row) || Math.Abs(x - @case.X) > double.Tolerance || row != @case.Row)
            {
                failures.Add($"{@case.Id}: parsed as ({x}, row {row}), expected ({@case.X}, row {@case.Row})");
            }

            var built = GestureIds.RowPlacement(@case.X, @case.Row);
            if (built != @case.Id)
            {
                failures.Add($"{@case.Id}: built back as {built}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void AnInvalidRowPlacement_IsRefused()
    {
        var accepted = Fixture.Placements.Row.Invalid
            .Where(@case => GestureIds.TryParseRowPlacement(@case.Id, out _, out _))
            .Select(@case => $"{@case.Id} ({@case.Why})")
            .ToList();

        Assert.Empty(accepted);
    }

    [Fact]
    public void AValidRelation_ParsesToItsEnds_AndBuildsBackToTheSameId()
    {
        var failures = new List<string>();
        foreach (var @case in Fixture.Relations.Valid)
        {
            if (!GestureIds.TryParseRelation(@case.Id, out var from, out var to) || from != @case.From || to != @case.To)
            {
                failures.Add($"{@case.Id}: parsed as ({from} -> {to}), expected ({@case.From} -> {@case.To})");
            }

            var built = GestureIds.Relation(@case.From, @case.To);
            if (built != @case.Id)
            {
                failures.Add($"{@case.Id}: built back as {built}");
            }
        }

        Assert.Empty(failures);
    }

    [Fact]
    public void AnInvalidRelation_IsRefused()
    {
        // R11.2 lives here: an empty source or target is refused, as causal-loop now refuses it too.
        var accepted = Fixture.Relations.Invalid
            .Where(@case => GestureIds.TryParseRelation(@case.Id, out _, out _))
            .Select(@case => $"{@case.Id} ({@case.Why})")
            .ToList();

        Assert.Empty(accepted);
    }

    [Fact]
    public void ThePrefixChecks_TellAPlacementFromARelation_WithoutParsing()
    {
        Assert.True(GestureIds.IsPlacement("new:1,2"));
        Assert.False(GestureIds.IsPlacement("rel:a->b"));
        Assert.False(GestureIds.IsPlacement(null));
        Assert.True(GestureIds.IsRelation("rel:a->b"));
        Assert.False(GestureIds.IsRelation("new:1,2"));
        Assert.False(GestureIds.IsRelation(null));
    }

    private static GestureFixture Load()
    {
        var path = IoPath.Combine(LocateRepositoryRoot(), "src", "fixtures", "cross-tier", "gesture-ids.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<GestureFixture>(File.ReadAllText(path), options)
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

    private sealed record GestureFixture(string Reason, PlacementCases Placements, CaseSet<RelationCase> Relations);

    private sealed record PlacementCases(CaseSet<PointCase> Xy, CaseSet<RowCase> Row);

    private sealed record CaseSet<TValid>(IReadOnlyList<TValid> Valid, IReadOnlyList<InvalidCase> Invalid);

    private sealed record PointCase(string Id, double X, double Y);

    private sealed record RowCase(string Id, double X, int Row);

    private sealed record RelationCase(string Id, string From, string To);

    private sealed record InvalidCase(string Id, string Why);
}
