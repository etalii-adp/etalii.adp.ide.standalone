using System.Globalization;
using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// The one place a timeline edit is run, and the one way a change is written: as a
/// <see cref="ModelChange"/> on the document's <see cref="TimelineBody"/>, which the binding plans as
/// splices (timeline-disl-fbl Requirement 3.1).
/// </summary>
/// <remarks>
/// <para>
/// <b>The run</b>: open the body of the text the store holds, refuse a document that could not be read
/// with today's sentence, read the model, run the handler's own checks (each with today's sentence,
/// Requirement 3.8) and the change it plans, store the text the body was left with, and answer with the
/// shared <see cref="RestoreDocumentCommand{TStore}"/> carrying the text before and the command as its
/// redo, so an undo puts the bytes back, a <c>connections:</c> key the edit created included
/// (Requirement 3.6).
/// </para>
/// <para>
/// <b>Nothing here builds a line of YAML.</b> Which keys an entry is written with, where a new one goes,
/// how a value is quoted and which relations a removal takes with it are the binding's; what stays here
/// is what the command was asked and why it may not be done.
/// </para>
/// <para>
/// Every handler locates against <b>current</b> state, because redo dispatches the same command
/// instance again later and the document has moved on since.
/// </para>
/// </remarks>
internal static class TimelineEdits
{
    /// <summary>
    /// Runs <paramref name="command"/> against its document in <paramref name="documents"/>, with
    /// <paramref name="edit"/> handed the body and the model read from it.
    /// </summary>
    public static CommandResult Run<TCommand>(
        ITimelineDocumentStore documents,
        TCommand command,
        Func<TimelineBody, TimelineModel, EditOutcome> edit)
        where TCommand : IDocumentEditCommand
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(edit);

        var entry = documents.GetOrLoad(command.BodyPath);
        if (!entry.IsUsable)
        {
            return CommandResult.Failure(
                $"{Path.GetFileName(command.BodyPath)} could not be read, so nothing can be edited until it can: {entry.Unreadable}");
        }

        var before = entry.Document.Text;
        var body = TimelineBody.Parse(before);
        var outcome = edit(body, Read(body));
        if (!outcome.WasApplied)
        {
            return CommandResult.Failure(outcome.Refusal!);
        }

        if (outcome.IsUnchanged)
        {
            return CommandResult.Success();
        }

        var saved = documents.Save(command.BodyPath, LineDocument.Parse(body.Text));
        return CommandResult.FromSave(saved, new RestoreDocumentCommand<ITimelineDocumentStore>(command.BodyPath, before, command));
    }

    /// <summary>
    /// The model of <paramref name="body"/> as a command reads it: through the binding, once YamlDotNet
    /// has accepted it (<see cref="TimelineYamlSyntax.Require"/>), so a command on a body that is not
    /// YAML meets what it always has.
    /// </summary>
    /// <exception cref="YamlDotNet.Core.YamlException">The body is not well-formed YAML.</exception>
    private static TimelineModel Read(TimelineBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        TimelineYamlSyntax.Require(body.Text);
        return TimelineReading.Of(body);
    }

    /// <summary>The element an id names in the current model, or null.</summary>
    public static TimelineElement? ElementOf(TimelineModel model, string elementId) =>
        model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, elementId, StringComparison.Ordinal));

    /// <summary>The connection an id names in the current model, or null.</summary>
    public static TimelineConnection? ConnectionOf(TimelineModel model, string connectionId) =>
        model.Connections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, connectionId, StringComparison.Ordinal));

    /// <summary>Sets <paramref name="attributes"/> of <paramref name="element"/>, as one change.</summary>
    /// <remarks>
    /// <para>
    /// An attribute the element's own rule does not bind is written by the rule of the other kind that
    /// matches the same entry: a moment given an end is written by the period rule's binding, after
    /// <c>begin</c> as its <c>keys</c> order it, and is a period from then on. No retype is planned.
    /// </para>
    /// <para>
    /// <b>A value that already reads as asked is not written</b>, so a drag that keeps an element's row
    /// leaves <c>row: "0"</c> as the author wrote it: an edit changes what was changed and nothing beside it.
    /// </para>
    /// </remarks>
    public static EditOutcome Set(TimelineBody body, TimelineElement element, params (string Name, object? Value)[] attributes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(element);
        return body.IdAt(element.Range, relation: false) is { } id
            ? Change(body, id, attributes)
            : EditOutcome.Refused(ElementGone);
    }

    /// <summary>Sets <paramref name="attributes"/> of <paramref name="connection"/>, as one change.</summary>
    public static EditOutcome Set(TimelineBody body, TimelineConnection connection, params (string Name, object? Value)[] attributes)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(connection);
        return body.IdAt(connection.Range, relation: true) is { } id
            ? Change(body, id, attributes)
            : EditOutcome.Refused(RelationGone);
    }

    /// <summary>The set of the attributes whose written text differs from what is asked, as one change; nothing to write is still an edit.</summary>
    private static EditOutcome Change(TimelineBody body, string id, (string Name, object? Value)[] attributes)
    {
        var read = body.Model.Find(id);
        var changed = attributes.Where(attribute => read is null || attribute.Value is null || !Holds(read, attribute.Name, attribute.Value)).ToArray();
        return changed.Length == 0 ? EditOutcome.Applied : body.Change(new ModelChange.Set(id, Values(changed)));
    }

    /// <summary>Whether the slot <paramref name="name"/> is written and reads as the text of <paramref name="value"/>.</summary>
    private static bool Holds(FblElement element, string name, object value) =>
        element.Attributes.ContainsKey(name) &&
        string.Equals(TimelineReading.TextOf(element, name), Convert.ToString(value, CultureInfo.InvariantCulture), StringComparison.Ordinal);

    /// <summary>
    /// Adds an element where the binding puts a new one: a period when it has an end, else a moment. The
    /// first element of a new timeline opens its <c>elements: []</c> into a block sequence.
    /// </summary>
    public static EditOutcome AddElement(TimelineBody body, string id, string label, string begin, string? end, int row)
    {
        ArgumentNullException.ThrowIfNull(body);
        var values = end is null
            ? Values(("label", label), ("begin", begin), ("row", row))
            : Values(("label", label), ("begin", begin), ("end", end), ("row", row));
        return body.Change(new ModelChange.Add(end is null ? "Moment" : "Period", id, values));
    }

    /// <summary>
    /// Adds a relation from <paramref name="from"/> to <paramref name="to"/>, each the id an element is
    /// written with, where the binding puts a new one: the first creates <c>connections:</c> at the end of
    /// the document. A label left empty is not written.
    /// </summary>
    public static EditOutcome AddConnection(TimelineBody body, string id, string from, string to, string label)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(label);
        var values = label.Length == 0
            ? Values(("from", from), ("to", to))
            : Values(("from", from), ("to", to), ("label", label));
        return body.Change(new ModelChange.Add("Connection", id, values));
    }

    /// <summary>Removes <paramref name="element"/>, and the relations whose <c>from</c> or <c>to</c> holds its written id, in one change.</summary>
    public static EditOutcome Remove(TimelineBody body, TimelineElement element)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(element);
        return body.IdAt(element.Range, relation: false) is { } id
            ? body.Change(new ModelChange.Remove(id))
            : EditOutcome.Refused(ElementGone);
    }

    /// <summary>Removes <paramref name="connection"/>; the <c>connections:</c> key stays, as the binding keeps it.</summary>
    public static EditOutcome Remove(TimelineBody body, TimelineConnection connection)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentNullException.ThrowIfNull(connection);
        return body.IdAt(connection.Range, relation: true) is { } id
            ? body.Change(new ModelChange.Remove(id))
            : EditOutcome.Refused(RelationGone);
    }

    /// <summary>What a command naming an element that is not in the document is told.</summary>
    public const string ElementGone = "That element is no longer in this timeline.";

    /// <summary>What a command naming a relation that is not in the document is told.</summary>
    public const string RelationGone = "That relation is no longer in this timeline.";

    private static Dictionary<string, object?> Values(params (string Name, object? Value)[] values) =>
        values.ToDictionary(value => value.Name, value => value.Value, StringComparer.Ordinal);
}
