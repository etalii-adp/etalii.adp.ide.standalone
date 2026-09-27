using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// The property rows of a selected trend or influence (Requirement 12), contributed as data the grid
/// renders without understanding - and the route a canvas resize, a chevron drag and a reattach reach
/// the document by.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every row is one of the client's ids</b>, stated once in its <c>ghgIds.ts</c>.
/// </para>
/// <para>
/// <b>Phases is a SLIDER over four ordered candidates</b> (Requirement 7.1), so a value outside the
/// sequence - a Trough without a Peak - cannot be expressed: the value is one of the four, never a
/// set of phases. A CHOICE would carry the same four strings and draw them as an unordered list, which
/// is the defect task 19's guard is seen to fail against.
/// </para>
/// <para>
/// <b>Rows the design does not list, and why they are here.</b> The context service lets a set reach
/// only a property its provider describes as editable, so every canvas gesture that edits through
/// <c>setProperty</c> needs a row: the three inner boundaries (a chevron drag), and each influence
/// end's attachment (a reattach). Start and Stop are the resize's rows, as the design says.
/// </para>
/// <para>
/// <b>Each phase lists its influences</b>, in a group of its own: the ones leaving it as
/// <i>Influence</i> and the ones arriving at it as <i>Influenced by</i>, one "Railways · Slope" a
/// line. Both are shown, not edited - an influence is drawn, reattached and deleted on the canvas. A
/// hidden phase is listed only while an influence still attaches to it, so nothing drawn from the
/// document goes missing from the grid.
/// </para>
/// <para>
/// <b>Read-only offers nothing writable</b> (Requirement 11.4): for a document that could not be read,
/// every row carries a read-only reason, and the service refuses a set to any of them.
/// </para>
/// </remarks>
public sealed class GhgContextPropertyProvider : IContextPropertyProvider
{
    public const string NameProperty = "ghg.name";
    public const string StartProperty = "ghg.start";
    public const string StopProperty = "ghg.stop";
    public const string PhasesProperty = "ghg.phases";
    public const string TagsProperty = "ghg.tags";
    public const string DescriptionProperty = "ghg.description";
    public const string FromProperty = "ghg.from";
    public const string ToProperty = "ghg.to";
    public const string FromAttachmentProperty = "ghg.from-attachment";
    public const string ToAttachmentProperty = "ghg.to-attachment";

    /// <summary>The row id of each inner boundary: <c>ghg.peak-end</c>, <c>ghg.trough-end</c>, <c>ghg.slope-end</c>.</summary>
    public static readonly IReadOnlyList<string> BoundaryProperties = [.. GhgPhases.BoundaryKeys.Select(key => $"ghg.{key}")];

    /// <summary>The row id of each phase's outgoing influences: <c>ghg.peak-influences</c> to <c>ghg.plateau-influences</c>.</summary>
    public static readonly IReadOnlyList<string> InfluencesProperties = [.. GhgPhases.Titles.Select(title => $"ghg.{title.ToLowerInvariant()}-influences")];

    /// <summary>The row id of each phase's incoming influences: <c>ghg.peak-influenced-by</c> to <c>ghg.plateau-influenced-by</c>.</summary>
    public static readonly IReadOnlyList<string> InfluencedByProperties = [.. GhgPhases.Titles.Select(title => $"ghg.{title.ToLowerInvariant()}-influenced-by")];

    /// <summary>What an influence list says when no influence attaches there.</summary>
    public const string NoInfluences = "None";

    /// <summary>The slider's four stops, in order; the value is the one at <c>phases - 1</c>.</summary>
    public static readonly IReadOnlyList<string> PhaseCandidates = ["Peak", "Peak and Trough", "Peak, Trough and Slope", "All four"];

    private const string IdentityGroup = "Identity";
    private const string TimeGroup = "Time";
    private const string PhasesGroup = "Phases";
    private const string EndsGroup = "Ends";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IGhgDocumentStore _documents;

    public GhgContextPropertyProvider(IHistoryStackStore historyStacks, IGhgDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);
        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Diagram.IsBody(target.ResolvedFullPath))
        {
            return Rows([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        var readOnly = entry.IsUsable ? "" : "The graph could not be read, so it cannot be edited.";
        var model = entry.Model;

        if (GhgEdits.TrendOf(model, target.ElementId) is { } trend)
        {
            List<ContextPropertyDefinition> rows =
            [
                new(NameProperty, "Name", trend.Name, ReadOnlyReason: readOnly, Group: IdentityGroup),
                new(DescriptionProperty, "Description", trend.Description, ContextPropertyEditor.Text, readOnly, IdentityGroup),
                new(TagsProperty, "Tags", string.Join(", ", trend.Tags), ReadOnlyReason: readOnly, Group: IdentityGroup),
                new(StartProperty, "Start", Month(trend.Start), ReadOnlyReason: readOnly, Group: TimeGroup),
                new(StopProperty, "Stop", Month(trend.Stop), ReadOnlyReason: readOnly, Group: TimeGroup),
                new(PhasesProperty, "Phases", PhaseCandidates[trend.VisiblePhases - 1], ContextPropertyEditor.Slider, readOnly, PhasesGroup, PhaseCandidates),
            ];

            // One row per DRAWN inner boundary: the ones a chevron can be dragged at.
            var drawn = GhgPhases.BoundariesOf(trend);
            for (var index = 0; index < drawn.Count; index++)
            {
                rows.Add(new(BoundaryProperties[index], $"{GhgPhases.Titles[index]} ends", GhgScale.FormatMonth(drawn[index]), ReadOnlyReason: readOnly, Group: PhasesGroup));
            }

            rows.AddRange(InfluenceRows(model, trend));
            return Rows(rows);
        }

        if (GhgEdits.InfluenceOf(model, target.ElementId) is { } influence)
        {
            const string shown = "Where it is attached; drag the end on the canvas to move it.";
            return Rows(
            [
                new(DescriptionProperty, "Description", influence.Description, ContextPropertyEditor.Text, readOnly, IdentityGroup),
                new(FromProperty, "From", Describe(model, influence.From, influence.FromEnd), ReadOnlyReason: shown, Group: EndsGroup),
                new(ToProperty, "To", Describe(model, influence.To, influence.ToEnd), ReadOnlyReason: shown, Group: EndsGroup),
                new(FromAttachmentProperty, "From attachment", influence.FromEnd.ToString(), ReadOnlyReason: readOnly, Group: EndsGroup),
                new(ToAttachmentProperty, "To attachment", influence.ToEnd.ToString(), ReadOnlyReason: readOnly, Group: EndsGroup),
            ]);
        }

        return Rows([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var body = target.ResolvedFullPath;
        var id = target.ElementId;
        var entry = _documents.GetOrLoad(body);
        if (!entry.IsUsable)
        {
            return ContextPropertyResult.Failure("The graph could not be read, so it cannot be edited.");
        }

        var model = entry.Model;
        var isTrend = GhgEdits.TrendOf(model, id) is not null;
        var isInfluence = GhgEdits.InfluenceOf(model, id) is not null;
        var boundary = IndexIn(BoundaryProperties, propertyId);

        ICommand? command;
        switch (propertyId)
        {
            case PhasesProperty when isTrend:
                var phases = IndexIn(PhaseCandidates, value.Trim()) + 1;
                if (phases == 0)
                {
                    return ContextPropertyResult.Failure($"'{value}' is not one of {string.Join(", ", PhaseCandidates)}.");
                }

                command = new SetGhgPhasesCommand(body, id, phases);
                break;
            case FromAttachmentProperty or ToAttachmentProperty when isInfluence:
                if (!GhgEnd.TryParse(value, out var end))
                {
                    return ContextPropertyResult.Failure($"'{value}' is not an attachment; write it as phase/edge/at, such as plateau/bottom/0.3.");
                }

                command = new SetGhgAttachmentCommand(body, id, propertyId == FromAttachmentProperty ? "from" : "to", end);
                break;
            default:
                command = propertyId switch
                {
                    NameProperty when isTrend => new RenameGhgTrendCommand(body, id, value),
                    StartProperty when isTrend => new SetGhgSpanCommand(body, id, value, null),
                    StopProperty when isTrend => new SetGhgSpanCommand(body, id, null, value),
                    TagsProperty when isTrend => new SetGhgTagsCommand(body, id, value),
                    DescriptionProperty when isTrend || isInfluence => new SetGhgDescriptionCommand(body, id, value),
                    _ when boundary >= 0 && isTrend => new SetGhgBoundaryCommand(body, id, boundary, value),
                    _ => null,
                };
                break;
        }

        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream - never written by the grid.
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    /// <summary>
    /// Per phase, its two influence lists: every drawn phase, and a hidden one only while an influence
    /// still attaches to it.
    /// </summary>
    private static IEnumerable<ContextPropertyDefinition> InfluenceRows(GhgModel model, GhgTrend trend)
    {
        const string shown = "Draw, reattach or delete an influence on the canvas.";
        for (var phase = 0; phase < GhgPhases.Titles.Count; phase++)
        {
            var leaving = model.Influences
                .Where(influence => influence.From == trend.Id && influence.FromEnd.PhaseIndex == phase)
                .Select(influence => Describe(model, influence.To, influence.ToEnd))
                .ToList();
            var arriving = model.Influences
                .Where(influence => influence.To == trend.Id && influence.ToEnd.PhaseIndex == phase)
                .Select(influence => Describe(model, influence.From, influence.FromEnd))
                .ToList();

            var drawn = phase < trend.VisiblePhases;
            if (!drawn && leaving.Count == 0 && arriving.Count == 0)
            {
                continue;
            }

            var group = drawn ? GhgPhases.Titles[phase] : $"{GhgPhases.Titles[phase]} (hidden)";
            yield return new(InfluencesProperties[phase], "Influence", List(leaving), ContextPropertyEditor.Text, shown, group);
            yield return new(InfluencedByProperties[phase], "Influenced by", List(arriving), ContextPropertyEditor.Text, shown, group);
        }
    }

    /// <summary>One entry a line, or <see cref="NoInfluences"/>.</summary>
    private static string List(IReadOnlyList<string> entries) => entries.Count == 0 ? NoInfluences : string.Join("\n", entries);

    /// <summary>An end as the grid shows it: "Steam engine · Plateau".</summary>
    private static string Describe(GhgModel model, string trendId, GhgEnd end)
    {
        var trend = GhgEdits.TrendOf(model, trendId);
        var name = trend is { Name.Length: > 0 } ? trend.Name : trendId;
        var phase = end.PhaseIndex >= 0 ? GhgPhases.Titles[end.PhaseIndex] : end.Phase;
        return $"{name} · {phase}";
    }

    private static int IndexIn(IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Month(int? month) => month is { } value ? GhgScale.FormatMonth(value) : "";

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
