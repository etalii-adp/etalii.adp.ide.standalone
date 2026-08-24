using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class ProblemMaintenanceTests : IDisposable
{
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(750);
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(15);

    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", ".mm");

    private readonly string _root;
    private readonly RecordingStore _store = new();
    private readonly CountingValidator _validator = new(Mindmap);
    private readonly ProblemMaintenance _maintenance;

    public ProblemMaintenanceTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var router = new DiagramFileRouter(new TestCatalog([MindmapDefinition]));
        var projectValidator = new ProjectValidator(router, new DiagramValidators([_validator]));
        _maintenance = new ProblemMaintenance(_store, projectValidator, router, SettleDelay);
    }

    public void Dispose()
    {
        _maintenance.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task AChangedDiagramCostsOneFilesValidation_NotATraversal()
    {
        CreatePair("a");
        CreatePair("b");
        _maintenance.Track(_root);

        File.AppendAllText(IoPath.Combine(_root, "a.mm"), " and more");

        var (_, payload) = await WaitForMutation("ReplaceFor");
        // One file changed, one validation - project-wide traversal would have made two.
        Assert.Equal(1, _validator.Calls);
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.Contains("a.adp", covered);
        Assert.DoesNotContain(covered, path => path.StartsWith('b'));
    }

    [Fact]
    public async Task ARemovedDiagramLosesItsEntries()
    {
        CreatePair("gone");
        _maintenance.Track(_root);

        File.Delete(IoPath.Combine(_root, "gone.adp"));

        var (_, payload) = await WaitForMutation("Remove");
        Assert.Equal("gone.adp", payload);
    }

    [Fact]
    public async Task ARenamedDiagramKeepsItsProblemsAtTheNewPath()
    {
        CreatePair("old");
        _maintenance.Track(_root);

        File.Move(IoPath.Combine(_root, "old.adp"), IoPath.Combine(_root, "new.adp"));

        var (_, payload) = await WaitForMutation("Move");
        Assert.Equal(("old.adp", "new.adp"), payload);
        // Moved, never dropped: the problems were not re-reported as unchecked.
        Assert.DoesNotContain(_store.Mutations, mutation => mutation.Kind == "Remove");
    }

    [Fact]
    public async Task RapidChangesCoalesceIntoOneValidation()
    {
        CreatePair("busy");
        _maintenance.Track(_root);

        for (var edit = 0; edit < 5; edit++)
        {
            File.AppendAllText(IoPath.Combine(_root, "busy.mm"), " more");
        }

        await WaitForMutation("ReplaceFor");
        await Task.Delay(SettleDelay + SettleDelay, TestContext.Current.CancellationToken);
        Assert.Equal(1, _validator.Calls);
    }

    [Fact]
    public async Task AChangeToAFileNoTypeClaims_IsIgnored()
    {
        CreatePair("real");
        _maintenance.Track(_root);

        File.WriteAllText(IoPath.Combine(_root, "notes.txt"), "just notes");

        // The real pair's change proves events flow; the .txt must not have caused anything.
        File.AppendAllText(IoPath.Combine(_root, "real.mm"), " and more");
        var (_, payload) = await WaitForMutation("ReplaceFor");
        var covered = Assert.IsType<IReadOnlyList<string>>(payload, exactMatch: false);
        Assert.DoesNotContain("notes.txt", covered);
        Assert.All(_store.Mutations, mutation => Assert.Equal("ReplaceFor", mutation.Kind));
    }

    // ---- plumbing ----------------------------------------------------------------------

    private void CreatePair(string baseName)
    {
        File.WriteAllText(IoPath.Combine(_root, baseName + ".adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(_root, baseName + ".mm"), "the document");
    }

    private async Task<(string Kind, object Payload)> WaitForMutation(string kind)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < WaitLimit)
        {
            var found = _store.Mutations.FirstOrDefault(mutation => mutation.Kind == kind);
            if (found.Kind is not null)
            {
                return found;
            }
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.Fail($"No {kind} mutation arrived within {WaitLimit}.");
        return default;
    }

    private sealed class RecordingStore : IProblemStore
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

    private sealed class TestCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
    }

    private sealed class CountingValidator(DiagramOrigin origin) : IDiagramValidator
    {
        private int _calls;

        public DiagramOrigin Origin { get; } = origin;
        public int Calls => _calls;

        public ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
            string document, string baseName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult<IReadOnlyList<DiagramProblem>>([]);
        }
    }
}
