using EtAlii.Adp.Diagram;

// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Problems;

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
    public static CachedProblem From(StoredProblem stored) => new(
        stored.Problem.Severity,
        stored.Problem.Message,
        stored.Problem.RuleId,
        (stored.Problem.Location as DiagramProblemElementLocation)?.Id,
        (stored.Problem.Location as DiagramProblemLineLocation)?.Number,
        stored.RelativePath,
        stored.LastWriteTimeUtc,
        stored.Length,
        stored.RulesVersion);

    public static StoredProblem ToStored(CachedProblem cached)
    {
        DiagramProblemLocation? location = cached.ElementId is not null
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
