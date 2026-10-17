using System.Globalization;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
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
/// adorner's handler applies. The scope, the could-not-be-read guard and the dispatch are the
/// frame's (<see cref="DiagramPropertyProvider{TBody, TModel}"/>).
/// </remarks>
public sealed class TimelineContextPropertyProvider(IHistoryStackStore historyStacks, ITimelineDocumentStore documents)
    : DiagramPropertyProvider<LineDocument, TimelineModel>(historyStacks, documents)
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

    /// <inheritdoc />
    protected override bool IsBody(string path) => Diagram.IsBody(path);

    /// <inheritdoc />
    /// <remarks>
    /// Derived from the definition's forms (TimelineDefinition.Rows). A moment has no End row:
    /// giving it an end is the context menu's action (Requirement 7.5), not a blank field inviting
    /// a value. A relation's From and To are read-only and say why.
    /// </remarks>
    protected override IReadOnlyList<ContextPropertyDefinition> Describe(DocumentEntry<LineDocument, TimelineModel> entry, string elementId) =>
        TimelineDefinition.Rows(entry.Model, elementId);

    /// <inheritdoc />
    /// <remarks>
    /// A value is validated before any command is made, so the refusal names the value rather than
    /// a command failure - and so an invalid value is never written (Requirement 10.3). The command
    /// goes through the project's history and out through the delta stream - never written by the
    /// panel (Requirement 10.5).
    /// </remarks>
    protected override DiagramPropertyChange Change(ContextTarget target, TimelineModel model, string propertyId, string value)
    {
        var element = TimelineEdits.ElementOf(model, target.ElementId);

        if (element is not null && propertyId is BeginProperty or EndProperty)
        {
            if (TimelineInstants.Parse(value) is null)
            {
                return DiagramPropertyChange.Refused($"'{value}' is not a time this timeline can read.");
            }

            if (MixesPrecision(element, propertyId, value))
            {
                // Requirement 3.2: one element never mixes a date-only value with a date-time
                // one. Refused here because the grid is the only path that can change one of the
                // pair independently.
                return DiagramPropertyChange.Refused(
                    "This element uses the other time form; begin and end must both be dates, or both carry a time.");
            }
        }

        return CommandFor(target, model, element, propertyId, value) is { } command
            ? DiagramPropertyChange.By(command)
            : DiagramPropertyChange.Refused($"'{propertyId}' cannot be edited on this selection.");
    }

    private static ICommand? CommandFor(ContextTarget target, TimelineModel model, TimelineElement? element, string propertyId, string value)
    {
        var body = target.ResolvedFullPath;
        var id = target.ElementId;

        if (element is null)
        {
            // A connection: only its label is editable, and the read-only rows explain why.
            return propertyId == LabelProperty && TimelineEdits.ConnectionOf(model, id) is not null
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
}
