using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The property rows of a selected element or connection, contributed as data the panel renders
/// without understanding (Requirement 10.1) - this module changes nothing in the panel.
/// </summary>
/// <remarks>
/// Begin and End use the <c>Line</c> editor with commit-time validation: no date-aware editor
/// exists yet, <c>property-grid</c> Requirement 9 owns that gap, and this module does not add a
/// private one (Requirement 10.6). The grid is the second of the two paths Requirement 3.4
/// closes - a value that would invert the element is refused here on exactly the terms the
/// adorner's handler applies.
/// </remarks>
public sealed class TimelineContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>The element's or connection's label row.</summary>
    public const string LabelProperty = "timeline.label";

    /// <summary>The element's begin row.</summary>
    public const string BeginProperty = "timeline.begin";

    /// <summary>The element's end row.</summary>
    public const string EndProperty = "timeline.end";

    /// <summary>The element's row row - the placement index, not a pixel.</summary>
    public const string RowProperty = "timeline.row";

    /// <summary>A connection's source, read-only.</summary>
    public const string FromProperty = "timeline.from";

    /// <summary>A connection's target, read-only.</summary>
    public const string ToProperty = "timeline.to";

    private readonly IHistoryStackStore _historyStacks;
    private readonly ITimelineDocumentStore _documents;

    /// <summary>Creates the provider over the history and the one document store.</summary>
    public TimelineContextPropertyProvider(IHistoryStackStore historyStacks, ITimelineDocumentStore documents)
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

        // Derived from the definition's forms (TimelineDefinition.Rows). A moment has no End row:
        // giving it an end is the context menu's action (Requirement 7.5), not a blank field
        // inviting a value. A relation's From and To are read-only and say why.
        return Rows(TimelineDefinition.Rows(_documents.GetOrLoad(target.ResolvedFullPath).Model, target.ElementId));
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var model = _documents.GetOrLoad(target.ResolvedFullPath).Model;
        var element = TimelineEdits.ElementOf(model, target.ElementId);

        // Validated before any command is made, so the refusal names the value rather than a
        // command failure - and so an invalid value is never written (Requirement 10.3).
        if (element is not null && propertyId is BeginProperty or EndProperty)
        {
            if (TimelineInstants.Parse(value) is null)
            {
                return ContextPropertyResult.Failure($"'{value}' is not a time this timeline can read.");
            }

            var mixes = MixesPrecision(element, propertyId, value);
            if (mixes)
            {
                // Requirement 3.2: one element never mixes a date-only value with a date-time
                // one. Refused here because the grid is the only path that can change one of the
                // pair independently.
                return ContextPropertyResult.Failure(
                    "This element uses the other time form; begin and end must both be dates, or both carry a time.");
            }
        }

        var command = CommandFor(target, element, propertyId, value);
        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream - never written by the
        // panel (Requirement 10.5).
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private ICommand? CommandFor(ContextTarget target, TimelineElement? element, string propertyId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        if (element is null)
        {
            // A connection: only its label is editable, and the read-only rows explain why.
            return propertyId == LabelProperty &&
                TimelineEdits.ConnectionOf(_documents.GetOrLoad(body).Model, id) is not null
                ? new RelabelTimelineConnectionCommand(body, id, value)
                : null;
        }

        return propertyId switch
        {
            LabelProperty => new RenameTimelineElementCommand(body, id, value),
            BeginProperty => new SetTimelinePlacementCommand(body, id, value, element.End?.Text, element.Row),
            EndProperty when element.End is not null => new SetTimelineEndCommand(body, id, value),
            RowProperty when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var row) =>
                new SetTimelinePlacementCommand(body, id, element.Begin.Text, element.End?.Text, row),
            _ => null,
        };
    }

    private static bool MixesPrecision(TimelineElement element, string propertyId, string value)
    {
        if (element.End is null)
        {
            // A moment has one time; there is nothing to disagree with.
            return false;
        }

        var typedIsDateTime = value.Contains('T', StringComparison.Ordinal);
        var otherIsDateTime = propertyId == BeginProperty
            ? element.End.Precision == TimelinePrecision.DateTime
            : element.Begin.Precision == TimelinePrecision.DateTime;

        return typedIsDateTime != otherIsDateTime;
    }

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
