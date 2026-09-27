using EtAlii.Adp.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The boundary between the module's vocabulary and core's: elements out, a placement back.
/// </summary>
public class TimelineElementMapperTests
{
    private readonly TimelineElementMapper _mapper = new();

    private static TimelineModel Parse(string yaml) =>
        TimelineParser.Parse(LineDocument.Parse(yaml));

    private const string TwoElementsAndAConnection = """
        timeline: 1
        elements:
          - id: aaa
            label: Period
            begin: 2026-01-05
            end: 2026-02-13
            row: 0
          - id: bbb
            label: Moment
            begin: 2026-02-16T14:00:00
            row: 2
        connections:
          - id: ccc
            from: aaa
            to: bbb
            label: gates
        """;

    [Fact]
    public void AnElementsPositionIsItsBeginAndItsRow_ThroughTheSharedConverters()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoElementsAndAConnection));

        // Assert.
        // Pinned through the converters rather than against magic numbers, because the mapper's
        // whole contract is that it does no arithmetic of its own.
        var period = elements.Single(element => element.Id == "aaa");
        Assert.Equal(TimelineScale.ToSeconds(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero)), period.X);
        Assert.Equal(TimelineRows.ToY(0), period.Y);

        var moment = elements.Single(element => element.Id == "bbb");
        Assert.Equal(TimelineScale.ToSeconds(new DateTimeOffset(2026, 2, 16, 14, 0, 0, TimeSpan.Zero)), moment.X);
        Assert.Equal(TimelineRows.ToY(2), moment.Y);
    }

    [Fact]
    public void APeriodAndAMomentCarryDifferentTypes()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoElementsAndAConnection));

        // Assert.
        // The canvas renders by type without reading the payload, so a moment typed as a period
        // would be drawn as a zero-width box - the thing Requirement 4.6 forbids.
        Assert.Equal(TimelineElementMapper.PeriodType, elements.Single(element => element.Id == "aaa").Type);
        Assert.Equal(TimelineElementMapper.MomentType, elements.Single(element => element.Id == "bbb").Type);
        Assert.Equal(TimelineElementMapper.ConnectionType, elements.Single(element => element.Id == "ccc").Type);
    }

    [Fact]
    public void ThePayloadCarriesTheTimesAsWritten()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoElementsAndAConnection));
        var payload = TimelineElementPayload.Parser.ParseFrom(
            elements.Single(element => element.Id == "aaa").Payload.Span);

        // Assert.
        Assert.Equal("2026-01-05", payload.Begin);
        Assert.Equal("2026-02-13", payload.End);
        Assert.Equal(0, payload.Row);
        Assert.True(payload.DateOnly);

        // The label travels in the payload because the core Element has none of its own. Its
        // absence surfaced as a rename that produced no wire change - the session's diff test
        // found it - so it is pinned here where the payload is inspected.
        Assert.Equal("Period", payload.Label);
    }

    [Fact]
    public void AConnectionCarriesItsEndpointsAndLabel()
    {
        // Act.
        var elements = _mapper.Elements(Parse(TwoElementsAndAConnection));
        var payload = TimelineConnectionPayload.Parser.ParseFrom(
            elements.Single(element => element.Id == "ccc").Payload.Span);

        // Assert.
        Assert.Equal("aaa", payload.FromElementId);
        Assert.Equal("bbb", payload.ToElementId);
        Assert.Equal("gates", payload.Label);
    }

    [Fact]
    public void AConnectionToNowhere_StillGoesOut()
    {
        // Arrange.
        // The validator reports it; the canvas marks it. Dropping it here would hide the problem
        // from both.
        var model = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-01-01\nconnections:\n  - id: c\n    from: a\n    to: ghost\n");

        // Act.
        var elements = _mapper.Elements(model);

        // Assert.
        Assert.Contains(elements, element => element.Id == "c");
    }

    [Fact]
    public void AnElementWithAnUnreadableBegin_IsStillDelivered()
    {
        // Arrange & act.
        var model = Parse("timeline: 1\nelements:\n  - id: broken\n    begin: not-a-date\n    row: 1\n");
        var elements = _mapper.Elements(model);

        // Assert.
        // Visible and selectable while the panel names the problem (Requirement 12.2), rather
        // than missing with no explanation on the canvas.
        var element = Assert.Single(elements);
        Assert.Equal(TimelineRows.ToY(1), element.Y);
    }

    [Fact]
    public void AnUnchangedRender_ProducesNoDeltas()
    {
        // Arrange.
        // The Same guard: ReadOnlyMemory equality compares references, so without it two
        // identical renders would re-deliver every element on every re-parse.
        var before = _mapper.Elements(Parse(TwoElementsAndAConnection));
        var after = _mapper.Elements(Parse(TwoElementsAndAConnection));

        // Act & assert.
        Assert.Empty(DiagramDiff.Between(before, after));
    }

    [Fact]
    public void AChangedElement_IsOneAdd_AndARemovedOneIsOneRemove()
    {
        // Arrange.
        // Edited line by line rather than by substring: a raw string literal carries the source
        // file's own line endings, so a "\n"-pattern replace works on an LF checkout and finds
        // nothing on a CRLF one - which is exactly how this test broke once.
        var before = _mapper.Elements(Parse(TwoElementsAndAConnection));
        var edited = string.Join(
            "\n",
            TwoElementsAndAConnection
                .ReplaceLineEndings("\n")
                .Split('\n')
                .Where(line => !line.Contains("bbb", StringComparison.Ordinal)
                    && !line.Contains("label: Moment", StringComparison.Ordinal)
                    && !line.Contains("2026-02-16", StringComparison.Ordinal)
                    && !line.Contains("row: 2", StringComparison.Ordinal))
                .Select(line => line.Replace("label: Period", "label: Renamed", StringComparison.Ordinal)));
        var after = _mapper.Elements(Parse(edited));

        // Act.
        var deltas = DiagramDiff.Between(before, after);

        // Assert.
        // An edit is an add carrying the new state; a removal is a remove naming the id.
        var removes = deltas.OfType<DiagramRemoveDelta>().Single();
        Assert.Contains("bbb", removes.ElementIds);
    }

    [Fact]
    public void ADraggedPeriod_KeepsItsDuration()
    {
        // Arrange.
        var model = Parse(TwoElementsAndAConnection);
        var period = model.Elements.Single(element => element.Id == "aaa");
        var newBegin = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);

        // Act.
        var (begin, end, row) = TimelineElementMapper.Placement(
            period, TimelineScale.ToSeconds(newBegin), TimelineRows.ToY(5));

        // Assert.
        // 2026-01-05 → 2026-02-13 is 39 days; the landing pair must be too (Requirement 6.1).
        Assert.Equal("2026-03-01", begin);
        Assert.Equal("2026-04-09", end);
        Assert.Equal(5, row);
    }

    [Fact]
    public void ADraggedDateOnlyElement_LandsOnADate()
    {
        // Arrange.
        var model = Parse(TwoElementsAndAConnection);
        var period = model.Elements.Single(element => element.Id == "aaa");
        var partWayThroughADay = TimelineScale.ToSeconds(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero)) + 7.3 * 3600;

        // Act.
        var (begin, _, _) = TimelineElementMapper.Placement(period, partWayThroughADay, 0);

        // Assert.
        Assert.Equal("2026-03-01", begin);
    }

    [Fact]
    public void ADraggedMoment_HasNoEndToInvent()
    {
        // Arrange.
        var model = Parse(TwoElementsAndAConnection);
        var moment = model.Elements.Single(element => element.Id == "bbb");

        // Act.
        var (begin, end, _) = TimelineElementMapper.Placement(
            moment, TimelineScale.ToSeconds(new DateTimeOffset(2026, 5, 1, 9, 30, 0, TimeSpan.Zero)), 0);

        // Assert.
        Assert.Equal("2026-05-01T09:30:00", begin);
        Assert.Null(end);
    }

    [Fact]
    public void ADraggedPeriodWithAnUnreadableEnd_MovesOnlyItsBegin()
    {
        // Arrange.
        // A duration nobody can compute cannot be preserved; inventing one would overwrite the
        // very value the panel is telling the user to fix.
        var model = Parse("timeline: 1\nelements:\n  - id: a\n    begin: 2026-01-01\n    end: not-a-date\n    row: 0\n");

        // Act.
        var (begin, end, _) = TimelineElementMapper.Placement(
            model.Elements[0], TimelineScale.ToSeconds(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero)), 0);

        // Assert.
        Assert.Equal("2026-02-01", begin);
        Assert.Equal("not-a-date", end);
    }

    /// <summary>
    /// This module keeps both an unfiltered <see cref="TimelineElementMapper.Elements"/> and a
    /// viewport-filtered <see cref="TimelineElementMapper.Visible"/> whose bodies are nearly the
    /// same. Two bodies with no stated relationship drift apart, so the relationship is stated.
    /// </summary>
    [Fact]
    public void TheUnfilteredMapping_MatchesTheUnboundedViewport()
    {
        // Arrange.
        var model = Parse(TwoElementsAndAConnection);

        // Act & assert: the same elements, in the same order.
        Assert.Equal(
            _mapper.Elements(model).Select(element => element.Id),
            _mapper.Visible(model, DiagramViewport.Unbounded).Select(element => element.Id));
    }

    /// <summary>
    /// And where the two part company, which is why they are not collapsed into one.
    /// </summary>
    /// <remarks>
    /// An unbounded viewport is not quite "everything": <see cref="TimelineElementMapper.Visible"/>
    /// withholds a connection unless both endpoints were sent, so the canvas is never asked to
    /// draw a curve to an element it does not hold. A connection naming an element the document
    /// never declares is therefore dropped by <c>Visible</c> at any viewport and kept by
    /// <c>Elements</c> - which matters, because <c>TimelineContextSourceResolver</c> resolves a
    /// selection by looking its id up in <c>Elements</c>. Collapsing the two would quietly make
    /// such a connection unselectable rather than being the pure refactor it looks like.
    /// </remarks>
    [Fact]
    public void TheTwoPartCompany_OverAConnectionToNothing()
    {
        // Arrange: a connection whose "to" names an element that is never declared.
        var model = Parse("""
            timeline: 1
            elements:
              - id: aaa
                label: Period
                begin: 2026-01-05
                row: 0
            connections:
              - id: ccc
                from: aaa
                to: nowhere
                label: dangles
            """);

        // Act.
        var unfiltered = _mapper.Elements(model).Select(element => element.Id).ToArray();
        var unbounded = _mapper.Visible(model, DiagramViewport.Unbounded).Select(element => element.Id).ToArray();

        // Assert.
        Assert.Contains("ccc", unfiltered);
        Assert.DoesNotContain("ccc", unbounded);
    }
}
