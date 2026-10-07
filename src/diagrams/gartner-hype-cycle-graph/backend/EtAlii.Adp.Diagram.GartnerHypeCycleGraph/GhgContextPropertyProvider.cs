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
/// <para>
/// <b>The rows are the definition's</b> (runtime plan step S11): <see cref="GhgDefinition.Rows"/> derives
/// them from the forms of the bundled DISL definition, with the ids of its <c>x-ghg</c> block. What a
/// set does stays here: <see cref="SetAsync"/> turns a row's text into the command that edits the document.
/// </para>
/// </remarks>
public sealed class GhgContextPropertyProvider : IContextPropertyProvider
{
    public const string NameProperty = "ghg.name";
    public const string DateProperty = "ghg.date";
    public const string TextProperty = "ghg.text";
    public const string SizeProperty = "ghg.size";
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

        return Rows(GhgDefinition.Rows(_documents.GetOrLoad(target.ResolvedFullPath), target.ElementId));
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
        var isTrigger = GhgEdits.TriggerOf(model, id) is not null;
        var isNote = GhgEdits.NoteOf(model, id) is not null;
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
                    NameProperty when isTrend || isTrigger => new RenameGhgElementCommand(body, id, value),
                    TextProperty when isNote => new RenameGhgElementCommand(body, id, value),
                    SizeProperty when isNote => new SetGhgNoteSizeCommand(body, id, value),
                    StartProperty when isTrend => new SetGhgSpanCommand(body, id, value, null),
                    StopProperty when isTrend => new SetGhgSpanCommand(body, id, null, value),
                    DateProperty when isTrigger => new SetGhgSpanCommand(body, id, value, null),
                    TagsProperty when isTrend || isTrigger => new SetGhgTagsCommand(body, id, value),
                    DescriptionProperty when isTrend || isTrigger || isInfluence => new SetGhgDescriptionCommand(body, id, value),
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

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
