using EtAlii.Adp.Problems;

namespace EtAlii.Adp.Problems.Tests;

internal sealed class ProblemBroadcasterStubProblemStore : IProblemStore
{
    public event Action<string>? Changed;

    public void RaiseChanged(string rootPath) => Changed?.Invoke(rootPath);

    public ProjectProblemSet Get(string rootPath) => new(ProjectProblemSetState.Validated, [], 0, 0, 0);

    public void Replace(string rootPath, IReadOnlyList<StoredProblem> problems) => throw new NotSupportedException();

    public void ReplaceFor(string rootPath, IReadOnlyList<string> relativePaths, IReadOnlyList<StoredProblem> problems) => throw new NotSupportedException();

    public void Remove(string rootPath, string relativePath) => throw new NotSupportedException();

    public void Move(string rootPath, string fromRelativePath, string toRelativePath) => throw new NotSupportedException();

    public void BeginValidating(string rootPath) { }

    public IReadOnlyList<string> KnownRoots() => [];
}
