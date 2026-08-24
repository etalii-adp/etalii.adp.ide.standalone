using EtAlii.Adp.Backend.Hierarchy;
using EtAlii.Adp.Backend.Problems;
using EtAlii.Adp.Diagram;

using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

public class StartupRevalidationTests : IDisposable
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramDefinition MindmapDefinition = new(Mindmap, "Mind map", ".mm");

    private readonly string _scratch;
    private readonly RecordingStore _store = new();
    private readonly GatedValidator _validator = new(Mindmap);
    private readonly ProjectValidator _projectValidator;

    public StartupRevalidationTests()
    {
        _scratch = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratch);
        var router = new DiagramFileRouter(new TestCatalog([MindmapDefinition]));
        _projectValidator = new ProjectValidator(router, new DiagramValidators([_validator]));
    }

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
        {
            Directory.Delete(_scratch, recursive: true);
        }
    }

    [Fact]
    public async Task EveryKnownProjectIsRevalidated_Sequentially()
    {
        var first = CreateProject("first");
        var second = CreateProject("second");
        _store.Roots = [first, second];
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        await revalidation.Completion.WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal([first, second], _store.Replaced);
    }

    [Fact]
    public async Task AnOpenedProjectJumpsTheQueue()
    {
        var first = CreateProject("first");
        var second = CreateProject("second");
        var third = CreateProject("third");
        _store.Roots = [first, second, third];
        _validator.HoldFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        while (_validator.Calls == 0)
        {
            await Task.Yield(); // The first project is mid-validation - the moment to open another.
        }
        revalidation.Prioritize(third);
        _validator.HoldFirstCall.SetResult();
        await revalidation.Completion.WaitAsync(TestContext.Current.CancellationToken);

        // The opened project's freshness is the one the user can see.
        Assert.Equal([first, third, second], _store.Replaced);
    }

    [Fact]
    public async Task AMissingRootIsSkippedOnce_NeverRetried()
    {
        var present = CreateProject("present");
        var missing = IoPath.Combine(_scratch, "not-there");
        _store.Roots = [missing, present];
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        await revalidation.Completion.WaitAsync(TestContext.Current.CancellationToken);

        // The queue drained - a retry loop would never have let Completion finish.
        Assert.Equal([present], _store.Replaced);
    }

    [Fact]
    public async Task StoppingAbandonsTheQueue()
    {
        var first = CreateProject("first");
        var second = CreateProject("second");
        _store.Roots = [first, second];
        _validator.HoldFirstCall = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var revalidation = new StartupRevalidation(_store, _projectValidator);

        await revalidation.StartAsync(TestContext.Current.CancellationToken);
        while (_validator.Calls == 0)
        {
            await Task.Yield();
        }
        var stopping = revalidation.StopAsync(TestContext.Current.CancellationToken);
        _validator.HoldFirstCall.SetResult();
        await stopping;

        Assert.DoesNotContain(second, _store.Replaced);
    }

    // ---- plumbing ----------------------------------------------------------------------

    private string CreateProject(string name)
    {
        var root = IoPath.Combine(_scratch, name);
        Directory.CreateDirectory(root);
        File.WriteAllText(IoPath.Combine(root, "flow.adp"), "freeplane/mindmap\n");
        File.WriteAllText(IoPath.Combine(root, "flow.mm"), "the document");
        return root;
    }

    private sealed class RecordingStore : IProblemStore
    {
        private readonly List<string> _replaced = [];

        public IReadOnlyList<string> Roots { get; set; } = [];

        public event Action<string>? Changed { add { } remove { } }

        public IReadOnlyList<string> Replaced
        {
            get { lock (_replaced) { return _replaced.ToArray(); } }
        }

        public ProjectProblemSet Get(string rootPath) => new(ProblemSetState.NeverValidated, [], 0, 0, 0);

        public void Replace(string rootPath, IReadOnlyList<StoredProblem> problems)
        {
            lock (_replaced) { _replaced.Add(rootPath); }
        }

        public void ReplaceFor(string rootPath, IReadOnlyList<string> relativePaths, IReadOnlyList<StoredProblem> problems) { }

        public void Remove(string rootPath, string relativePath) { }

        public void Move(string rootPath, string fromRelativePath, string toRelativePath) { }

        public IReadOnlyList<string> KnownRoots() => Roots;
    }

    private sealed class TestCatalog(IReadOnlyList<DiagramDefinition> definitions) : IDiagramDefinitionCatalog
    {
        public IReadOnlyList<DiagramDefinition> All { get; } = definitions;
    }

    private sealed class GatedValidator(DiagramOrigin origin) : IDiagramValidator
    {
        private int _calls;

        public DiagramOrigin Origin { get; } = origin;
        public int Calls => _calls;
        public TaskCompletionSource? HoldFirstCall { get; set; }

        public async ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
            string document, string baseName, CancellationToken cancellationToken)
        {
            var call = Interlocked.Increment(ref _calls);
            if (call == 1 && HoldFirstCall is not null)
            {
                await HoldFirstCall.Task;
            }
            return [];
        }
    }
}
