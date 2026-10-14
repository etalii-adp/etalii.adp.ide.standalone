using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Timeline;

/// <summary>
/// What every handler does first: locate against <b>current</b> state, because undo and redo
/// dispatch the same command instance again later and the document has moved on since.
/// </summary>
internal static class TimelineEdits
{
    /// <summary>
    /// The model of <paramref name="body"/> as a command reads it: through the binding, once YamlDotNet
    /// has accepted it (<see cref="TimelineYamlSyntax.Require"/>), so a command on a body that is not
    /// YAML meets what it always has.
    /// </summary>
    /// <exception cref="YamlDotNet.Core.YamlException">The body is not well-formed YAML.</exception>
    public static TimelineModel Read(LineDocument body)
    {
        TimelineYamlSyntax.Require(body.Text);
        return TimelineReading.Of(body.Text);
    }

    /// <summary>The element an id names in the current model, or null.</summary>
    public static TimelineElement? ElementOf(TimelineModel model, string elementId) =>
        model.Elements.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, elementId, StringComparison.Ordinal));

    /// <summary>The connection an id names in the current model, or null.</summary>
    public static TimelineConnection? ConnectionOf(TimelineModel model, string connectionId) =>
        model.Connections.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, connectionId, StringComparison.Ordinal));

    /// <summary>The lines a range covers, captured for a byte-exact restore.</summary>
    public static LineSegment Capture(LineDocument document, LineRange range) =>
        new(
            range.Start,
            Enumerable.Range(range.Start, range.Length).Select(i => document.Lines[i].Text).ToList());
}
