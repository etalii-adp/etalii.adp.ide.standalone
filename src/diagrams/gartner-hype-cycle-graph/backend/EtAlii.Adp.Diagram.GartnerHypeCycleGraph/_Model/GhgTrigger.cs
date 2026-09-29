using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>One trigger entry - a moment in time that set trends off - and the lines that declare it.</summary>
/// <param name="Id">Its id. Empty when the entry states none, which the rules report.</param>
/// <param name="Name">Its name, drawn left of its circle.</param>
/// <param name="Date">The month it happened, as a month index; null when missing or malformed, which <c>ghg.trigger-date</c> reports.</param>
/// <param name="Row">Its row; its centre is on the row's middle.</param>
/// <param name="Tags">Its tags, in document order.</param>
/// <param name="Description">Prose about the trigger. <b>Never sent to the client</b>.</param>
/// <param name="Range">The lines this entry occupies, which is what an edit rewrites and nothing else.</param>
/// <remarks>
/// A trigger has no span and no phases: it is a point on the time axis, and only ever the source
/// of an influence. An influence from one carries an empty <see cref="GhgInfluence.FromEnd"/>.
/// </remarks>
public sealed record GhgTrigger(
    string Id,
    string Name,
    int? Date,
    int Row,
    IReadOnlyList<string> Tags,
    string Description,
    LineRange Range);
