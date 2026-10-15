using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Everything one <c>.ghg</c> document declares: its trends, triggers, notes and influences, and what the parser
/// could not read.
/// </summary>
/// <param name="Trends">The trend entries, in document order.</param>
/// <param name="Influences">The influence entries, in document order.</param>
/// <param name="Problems">What the parser passed over, each with the line that caused it.</param>
/// <param name="Version">The header's version, or <c>null</c> when the header is missing or unreadable.</param>
/// <param name="Unit">The step the time axis is drawn in; <see cref="GhgTimeUnit.Month"/> when the document names none.</param>
/// <remarks>
/// The problems travel with the model rather than being thrown, as in FDG: a document with a bad
/// entry still draws every entry that is good. There is no "is valid" here; validity is the rule
/// set's answer.
/// </remarks>
public sealed record GhgModel(
    IReadOnlyList<GhgTrend> Trends,
    IReadOnlyList<GhgInfluence> Influences,
    IReadOnlyList<ParseProblem> Problems,
    int? Version,
    GhgTimeUnit? Unit = null)
{
    /// <summary>The trigger entries, in document order; empty when the document has no <c>triggers</c> list.</summary>
    public IReadOnlyList<GhgTrigger> Triggers { get; init; } = [];

    /// <summary>The note entries, in document order; empty when the document has no <c>notes</c> list.</summary>
    public IReadOnlyList<GhgNote> Notes { get; init; } = [];

    /// <summary>A document that declares nothing - the parser's answer to text it could not read at all.</summary>
    public static GhgModel Empty { get; } = new([], [], [], null);

    /// <summary>The trend with <paramref name="id"/>: the first entry with it, as the canvas draws it.</summary>
    public GhgTrend? TrendOf(string id) =>
        Trends.FirstOrDefault(trend => string.Equals(trend.Id, id, StringComparison.Ordinal));

    /// <summary>The trigger with <paramref name="id"/>: the first entry with it.</summary>
    public GhgTrigger? TriggerOf(string id) =>
        Triggers.FirstOrDefault(trigger => string.Equals(trigger.Id, id, StringComparison.Ordinal));

    /// <summary>The note with <paramref name="id"/>: the first entry with it.</summary>
    public GhgNote? NoteOf(string id) =>
        Notes.FirstOrDefault(note => string.Equals(note.Id, id, StringComparison.Ordinal));

    /// <summary>The influence with <paramref name="id"/>: the first entry with it.</summary>
    public GhgInfluence? InfluenceOf(string id) =>
        Influences.FirstOrDefault(influence => string.Equals(influence.Id, id, StringComparison.Ordinal));

    /// <summary>Whether any entry of any list already has <paramref name="id"/>: ids are unique across all four.</summary>
    public bool IsTaken(string id) =>
        Trends.Any(trend => trend.Id == id) ||
        Triggers.Any(trigger => trigger.Id == id) ||
        Notes.Any(note => note.Id == id) ||
        Influences.Any(influence => influence.Id == id);

    /// <summary>Whether <paramref name="id"/> names a trend, a trigger or a note: something drawn as an element.</summary>
    public bool IsElement(string id) =>
        TrendOf(id) is not null || TriggerOf(id) is not null || NoteOf(id) is not null;

    /// <summary>The step the time axis is drawn in, never null.</summary>
    public GhgTimeUnit TimeUnit => Unit ?? GhgTimeUnit.Month;

    /// <summary>The version this module writes, and the only one it understands.</summary>
    public const int CurrentVersion = 1;
}
