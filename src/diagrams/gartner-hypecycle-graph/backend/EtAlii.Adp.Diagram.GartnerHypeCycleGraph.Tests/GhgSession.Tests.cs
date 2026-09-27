using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph.Tests;

/// <summary>
/// Task 17: a session over the technology-trends example, through the real store on a real file.
/// </summary>
/// <remarks>
/// Every change is made the way an edit makes it - with <see cref="GhgWriter"/>, written to disk, then
/// reloaded through the store - so the path under test is the file, the lifecycle, the change handler
/// and the mapper. An absence is only asserted beside a presence.
/// </remarks>
public sealed class GhgSessionTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.GhgSessionTests", Guid.NewGuid().ToString("N"));
    private readonly GhgDocumentStore _store = new();
    private readonly List<DiagramDeltasEventArgs> _raised = [];

    public GhgSessionTests()
    {
        Directory.CreateDirectory(_folder);
        File.Copy(GhgModuleFiles.Example, Body);
    }

    private string Body => Path.Combine(_folder, "technology-trends.ghg");

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

    /// <summary>The guard: every trend arrives, with the boundaries <see cref="GhgPhases"/> computed for it.</summary>
    [Fact]
    public async Task AFreshSession_DeliversEveryTrendWithItsComputedBoundaries()
    {
        var model = Parse();
        await using var session = Open();

        var delivered = Delivered(session.Baseline());

        var trends = delivered.Where(element => element.Type == GhgElementMapper.TrendType).ToDictionary(element => element.Id);
        Assert.Equal(model.Trends.Count, trends.Count);
        Assert.Equal(model.Influences.Count, delivered.Count(element => element.Type == GhgElementMapper.InfluenceType));
        Assert.All(model.Trends, trend =>
        {
            var payload = TrendPayload(trends[trend.Id]);
            Assert.Equal(trend.Name, payload.Name);
            Assert.Equal(trend.Phases, payload.Phases);
            Assert.Equal(GhgPhases.FractionsOf(trend), payload.Boundaries);
            Assert.Equal(trend.Tags, payload.Tags);
            Assert.Equal(GhgScale.WidthOf(trend.Months), payload.Width);
        });

        // A trend with a dragged boundary arrives with it, not with an even split.
        var steam = model.Trends.Single(trend => trend.Id == "steam-engine");
        var fractions = TrendPayload(trends["steam-engine"]).Boundaries;
        Assert.Equal((GhgScale.MonthIndex(1800, 1) - steam.Start!.Value) / (double)steam.Months, fractions[1], 9);
    }

    /// <summary>
    /// Requirement 7.2: an influence attached to a hidden phase is still sent, unchanged. Seen to fail
    /// against a mapper that drops influences on hidden phases.
    /// </summary>
    [Fact]
    public async Task AnInfluenceOnAHiddenPhase_IsStillDelivered()
    {
        var model = Parse();
        var hidden = model.Influences.Single(influence =>
            model.Trends.SingleOrDefault(trend => trend.Id == influence.From) is { } from && influence.FromEnd.PhaseIndex >= from.VisiblePhases);
        await using var session = Open();

        var delivered = Delivered(session.Baseline()).Single(element => element.Id == hidden.Id);

        var payload = GhgInfluencePayload.Parser.ParseFrom(delivered.Payload.ToArray());
        Assert.Equal(hidden.From, payload.FromElementId);
        Assert.Equal(hidden.FromEnd.PhaseIndex, payload.SourceAttachment.Region);
        Assert.Equal(hidden.FromEnd.Edge, payload.SourceAttachment.Edge);
        Assert.Equal(hidden.FromEnd.At!.Value, payload.SourceAttachment.At);
    }

    /// <summary>
    /// Requirement 12.3: a Description is never sent, so a change to one alone raises nothing - beside
    /// a name change through the same path that does. Seen to fail against a mapper that packs the
    /// Description into the payload.
    /// </summary>
    [Fact]
    public async Task ADescriptionChangedOnDisk_RaisesNothing_WhileANameChangeDoes()
    {
        await using var session = Open();
        session.Baseline();

        Edit(document => GhgWriter.SetDescription(document, Trend(document, "railways"), "Something else entirely."));

        Assert.Empty(_raised);

        Edit(document => GhgWriter.SetName(document, Trend(document, "railways"), "Railroads"));

        var changed = Assert.Single(Assert.IsType<DiagramAddDelta>(Assert.Single(Assert.Single(_raised).Deltas)).Elements);
        Assert.Equal("Railroads", TrendPayload(changed).Name);
    }

    /// <summary>A phase count lowered on disk arrives as a delta, and the hidden influences stay delivered.</summary>
    [Fact]
    public async Task APhaseCountLoweredOnDisk_ArrivesAsADelta()
    {
        await using var session = Open();
        var before = Delivered(session.Baseline()).Count;

        Edit(document => GhgWriter.SetPhases(document, Trend(document, "steam-engine"), 1));

        var changed = Assert.Single(Assert.IsType<DiagramAddDelta>(Assert.Single(Assert.Single(_raised).Deltas)).Elements);
        Assert.Equal(1, TrendPayload(changed).Phases);
        Assert.Empty(TrendPayload(changed).Boundaries);
        Assert.Equal(before, new GhgElementMapper().Visible(Parse(), DiagramViewport.Unbounded).Count);
    }

    [Fact]
    public void NeitherPayload_HasAFieldForADescription()
    {
        Assert.DoesNotContain(GhgTrendPayload.Descriptor.Fields.InDeclarationOrder(), field => field.Name.Contains("description", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(GhgInfluencePayload.Descriptor.Fields.InDeclarationOrder(), field => field.Name.Contains("description", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The definition this module declares is the one discovery reads.</summary>
    [Fact]
    public void TheDefinition_IsDeclaredForDiscovery()
    {
        var definition = Assert.Single(Diagram.Definitions);

        Assert.Equal("gartner/hypecycle-graph", definition.Origin.Key);
        Assert.Equal(".ghg", definition.Extension);
        Assert.NotNull(definition.Build);
    }

    private GhgSession Open()
    {
        var session = new GhgSession(Body, _store, new GhgElementMapper());
        session.Changed += (_, args) => _raised.Add(args);
        return session;
    }

    private GhgModel Parse() => GhgParser.Parse(LineDocument.Parse(File.ReadAllText(Body)));

    private static GhgTrend Trend(LineDocument document, string id) =>
        GhgParser.Parse(document).Trends.Single(trend => trend.Id == id);

    private void Edit(Func<LineDocument, GhgEdit> edit)
    {
        var document = LineDocument.Parse(File.ReadAllText(Body));
        Assert.True(edit(document).WasApplied);
        File.WriteAllText(Body, document.Text);
        _store.Reload(Body);
    }

    private static IReadOnlyList<DiagramElement> Delivered(IReadOnlyList<DiagramDelta> baseline) =>
        Assert.IsType<DiagramAddDelta>(Assert.Single(baseline)).Elements;

    private static GhgTrendPayload TrendPayload(DiagramElement element) =>
        GhgTrendPayload.Parser.ParseFrom(element.Payload.ToArray());
}
