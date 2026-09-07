namespace EtAlii.Adp.Problems.Tests;

internal sealed class StartupRevalidationRecordingStore : IProblemStore
{
    private readonly List<string> _replaced = [];

    public IReadOnlyList<string> Roots { get; set; } = [];

    public event Action<string>? Changed { add { } remove { } }

    public IReadOnlyList<string> Replaced
    {
        get { lock (_replaced) { return _replaced.ToArray(); } }
    }

    public ProjectProblemSet Get(string rootPath) => new(ProjectProblemSetState.NeverValidated, [], 0, 0, 0);

    public void Replace(string rootPath, IReadOnlyList<StoredProblem> problems)
    {
        lock (_replaced) { _replaced.Add(rootPath); }
    }

    public void ReplaceFor(string rootPath, IReadOnlyList<string> relativePaths, IReadOnlyList<StoredProblem> problems) { }

    public void Remove(string rootPath, string relativePath) { }

    public void Move(string rootPath, string fromRelativePath, string toRelativePath) { }

    public void BeginValidating(string rootPath) { }

    public IReadOnlyList<string> KnownRoots() => Roots;
}
