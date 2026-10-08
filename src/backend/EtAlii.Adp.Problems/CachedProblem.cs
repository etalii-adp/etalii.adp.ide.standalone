using EtAlii.Adp.Diagram;
using JetBrains.Annotations;

// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

internal sealed record CachedProblem(
    DiagramProblemSeverity Severity,
    string Message,
    string RuleId,
    string? ElementId,
    uint? Line,
    string RelativePath,
    DateTime LastWriteTimeUtc,
    long Length,
    string RulesVersion)
{
    /// <summary>
    /// The file a <see cref="DiagramProblemFileLocation"/> named, when the problem carried one.
    /// Kept apart from <see cref="Line"/>'s case rather than reusing it: a line location means
    /// "line N of the diagram's own document", and this means "line N of that other file", and
    /// a cache that collapsed the two would reopen the wrong file after a restart.
    /// </summary>
    /// <remarks>
    /// Nullable with a default so a cache file written before this existed still deserializes.
    /// </remarks>
    [UsedImplicitly] // Written and read by System.Text.Json in the problem cache file (ProblemStore, ProblemCacheFile); private, it would not round-trip.
    public string? FilePath { get; init; }

    public static CachedProblem From(StoredProblem stored) => new(
        stored.Problem.Severity,
        stored.Problem.Message,
        stored.Problem.RuleId,
        (stored.Problem.Location as DiagramProblemElementLocation)?.Id,
        (stored.Problem.Location as DiagramProblemLineLocation)?.Number
            ?? (stored.Problem.Location as DiagramProblemFileLocation)?.Line,
        stored.RelativePath,
        stored.LastWriteTimeUtc,
        stored.Length,
        stored.RulesVersion)
    {
        FilePath = (stored.Problem.Location as DiagramProblemFileLocation)?.RelativePath,
    };

    public static StoredProblem ToStored(CachedProblem cached)
    {
        DiagramProblemLocation? location = cached.FilePath is not null
            ? new DiagramProblemFileLocation(cached.FilePath, cached.Line ?? 0)
            : cached.ElementId is not null
            ? new DiagramProblemElementLocation(cached.ElementId)
            : cached.Line is { } line ? new DiagramProblemLineLocation(line) : null;
        return new StoredProblem(
            new DiagramProblem(cached.Severity, cached.Message, cached.RuleId, location),
            cached.RelativePath,
            cached.LastWriteTimeUtc,
            cached.Length,
            cached.RulesVersion);
    }
}
