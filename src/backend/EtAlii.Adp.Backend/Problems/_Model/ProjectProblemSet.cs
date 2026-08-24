namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// Everything currently known to be wrong in one project - what the store holds and the
/// panel shows.
/// </summary>
/// <param name="State">Whether the list is an answer, an old answer being refreshed, or no answer yet.</param>
/// <param name="Problems">The problems, possibly bounded; see <paramref name="TruncatedAt"/>.</param>
/// <param name="ErrorCount">Errors in the whole set - counted before any truncation.</param>
/// <param name="WarningCount">Warnings in the whole set - counted before any truncation.</param>
/// <param name="TruncatedAt">
/// The bound <paramref name="Problems"/> was cut at, or 0 when nothing was cut. The counts
/// stay those of the whole set, so the panel can say how many are not shown (Requirement 5.6).
/// </param>
public sealed record ProjectProblemSet(
    ProjectProblemSetState State,
    IReadOnlyList<StoredProblem> Problems,
    int ErrorCount,
    int WarningCount,
    int TruncatedAt);
