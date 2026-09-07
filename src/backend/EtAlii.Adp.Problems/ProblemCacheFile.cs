// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

/// <summary>The cache's own shape - flat, so the abstract location needs no JSON polymorphism.</summary>
internal sealed record ProblemCacheFile(int Version, string RootPath, IReadOnlyList<CachedProblem> Problems);
