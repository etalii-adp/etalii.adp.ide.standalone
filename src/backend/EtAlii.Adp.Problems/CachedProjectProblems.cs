// EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Problems;

internal sealed class CachedProjectProblems(string rootPath)
{
    public object Gate { get; } = new();
    public string RootPath { get; } = rootPath;
    public List<StoredProblem> Problems { get; } = [];
    public ProjectProblemSetState State { get; set; } = ProjectProblemSetState.NeverValidated;
    public Timer? WriteTimer { get; set; }
}
