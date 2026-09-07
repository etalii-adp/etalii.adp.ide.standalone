namespace EtAlii.Adp.Problems.Tests;

internal sealed class ProblemMaintenanceRecordingStore : IProblemStore
{
    private readonly List<(string Kind, object Payload)> _mutations = [];

    public event Action<string>? Changed { add { } remove { } }

    public IReadOnlyList<(string Kind, object Payload)> Mutations
    {
        get { lock (_mutations) { return _mutations.ToArray(); } }
    }

    public ProjectProblemSet Get(string rootPath) => new(ProjectProblemSetState.NeverValidated, [], 0, 0, 0);

    public void Replace(string rootPath, IReadOnlyList<StoredProblem> problems) => Record("Replace", problems);

    public void ReplaceFor(string rootPath, IReadOnlyList<string> relativePaths, IReadOnlyList<StoredProblem> problems) =>
        Record("ReplaceFor", relativePaths);

    public void Remove(string rootPath, string relativePath) => Record("Remove", relativePath);

    public void Move(string rootPath, string fromRelativePath, string toRelativePath) =>
        Record("Move", (fromRelativePath, toRelativePath));

    public void BeginValidating(string rootPath) { }

    public IReadOnlyList<string> KnownRoots() => [];

    private void Record(string kind, object payload)
    {
        lock (_mutations) { _mutations.Add((kind, payload)); }
    }
}
