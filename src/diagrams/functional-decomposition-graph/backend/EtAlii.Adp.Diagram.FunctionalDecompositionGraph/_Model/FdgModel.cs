using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>
/// Everything one <c>.fdg</c> document declares: its elements, its connections, and what the
/// parser could not read.
/// </summary>
/// <param name="Elements">The element entries, in document order.</param>
/// <param name="Connections">The connection entries, in document order.</param>
/// <param name="Problems">What the parser passed over, each with the line that caused it.</param>
/// <param name="Version">The header's version, or <c>null</c> when the header is missing or unreadable.</param>
/// <remarks>
/// <para>
/// <b>The problems travel with the model rather than being thrown.</b> A document with a bad entry
/// still draws every entry that is good, so the parser records what it could not read and hands
/// both halves back. Requirement 2.2 is that the lines survive; this is how the validator learns
/// there was something to report without re-reading the text.
/// </para>
/// <para>
/// <b>There is no "is valid" on this model.</b> Validity is the rule set's answer, computed from
/// the whole document, and a model that carried its own verdict would be a second place for it to
/// be wrong.
/// </para>
/// </remarks>
public sealed record FdgModel(
    IReadOnlyList<FdgElement> Elements,
    IReadOnlyList<FdgConnection> Connections,
    IReadOnlyList<ParseProblem> Problems,
    int? Version)
{
    /// <summary>A document that declares nothing - the parser's answer to text it could not read at all.</summary>
    public static FdgModel Empty { get; } = new([], [], [], null);

    /// <summary>The element with <paramref name="id"/>: the first entry with it, as the canvas draws it.</summary>
    public FdgElement? ElementOf(string id) =>
        Elements.FirstOrDefault(element => string.Equals(element.Id, id, StringComparison.Ordinal));

    /// <summary>The connection with <paramref name="id"/>: the first entry with it.</summary>
    public FdgConnection? ConnectionOf(string id) =>
        Connections.FirstOrDefault(connection => string.Equals(connection.Id, id, StringComparison.Ordinal));

    /// <summary>Whether any element or connection already uses <paramref name="id"/>.</summary>
    public bool IsTaken(string id) =>
        Elements.Any(element => element.Id == id) || Connections.Any(connection => connection.Id == id);

    /// <summary>The version this module writes, and the only one it understands.</summary>
    public const int CurrentVersion = 1;
}
