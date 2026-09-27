namespace EtAlii.Adp.Diagram.GartnerHypeCycleGraph;

/// <summary>
/// Everything one <c>.ghg</c> document declares: its trends, its influences, and what the parser
/// could not read.
/// </summary>
/// <param name="Trends">The trend entries, in document order.</param>
/// <param name="Influences">The influence entries, in document order.</param>
/// <param name="Problems">What the parser passed over, each with the line that caused it.</param>
/// <param name="Version">The header's version, or <c>null</c> when the header is missing or unreadable.</param>
/// <remarks>
/// The problems travel with the model rather than being thrown, as in FDG: a document with a bad
/// entry still draws every entry that is good. There is no "is valid" here; validity is the rule
/// set's answer.
/// </remarks>
public sealed record GhgModel(
    IReadOnlyList<GhgTrend> Trends,
    IReadOnlyList<GhgInfluence> Influences,
    IReadOnlyList<GhgProblem> Problems,
    int? Version)
{
    /// <summary>A document that declares nothing - the parser's answer to text it could not read at all.</summary>
    public static GhgModel Empty { get; } = new([], [], [], null);

    /// <summary>The version this module writes, and the only one it understands.</summary>
    public const int CurrentVersion = 1;
}

/// <summary>Something the parser could not read, and where.</summary>
/// <param name="Line">The zero-based line it was found on.</param>
/// <param name="Message">What could not be read, in a sentence meant for the panel.</param>
public sealed record GhgProblem(int Line, string Message);
