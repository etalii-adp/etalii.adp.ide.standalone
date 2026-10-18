using EtAlii.Adp.Diagram;
using EtAlii.Adp.Editor;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// FR-066: the model reads no disk under its lock, so a slow read holds up no other caller. The
/// slow read is a registration's type being looked up while a folder's entries are decided: the
/// catalog the routers ask stops until the test lets it go, and while it is stopped another thread
/// asks the model a question that needs only its lock.
/// </summary>
public class HierarchyModelDiskOutsideTheLockTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new ToolOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");
    private static readonly EditorDefinition Plain = new("plain", "Plain Text", IsFallback: true);

    // Far longer than an uncontended lock takes on a loaded runner, so a timeout is the lock.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(2);

    // How long the read is allowed to stay stopped before the test gives up on it.
    private static readonly TimeSpan Stopped = TimeSpan.FromSeconds(30);

    private readonly string _root;
    private readonly StoppingCatalog _catalog = new(Mindmap);

    public HierarchyModelDiskOutsideTheLockTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        _catalog.Release();
        TestFolder.TryDelete(_root);
    }

    /// <summary>A catalog that, once armed, stops the next caller until it is released.</summary>
    private sealed class StoppingCatalog(params DiagramDefinition[] definitions) : IDiagramDefinitionCatalog
    {
        private readonly ManualResetEventSlim _released = new(initialState: true);
        private int _armed;

        public ManualResetEventSlim Entered { get; } = new();

        public IReadOnlyList<DiagramDefinition> All
        {
            get
            {
                if (Interlocked.Exchange(ref _armed, 0) == 1)
                {
                    Entered.Set();
                    _released.Wait(Stopped);
                }

                return definitions;
            }
        }

        public void Arm()
        {
            _released.Reset();
            Volatile.Write(ref _armed, 1);
        }

        public void Release() => _released.Set();
    }

    public static TheoryData<string> EntryPoints =>
    [
        "ListChildren",
        "Reconcile",
        "OnWatcherEvent",
        "ApplyLocalRename",
    ];

    [Theory]
    [MemberData(nameof(EntryPoints))]
    public void ASlowRead_HoldsUpNoConcurrentQuery(string entryPoint)
    {
        // Arrange.
        var model = new HierarchyModel(_root, TestToolRouters.For(_catalog, editors: [Plain]));
        var act = Arrange(entryPoint, model);
        _catalog.Arm();

        // Act: the entry point reads, and stops on the way; meanwhile another thread asks.
        var acting = new Thread(() => act()) { IsBackground = true };
        acting.Start();
        var read = _catalog.Entered.Wait(Stopped, TestContext.Current.CancellationToken);
        var answered = read && AnswersFromAnotherThread(model);
        _catalog.Release();
        var finished = acting.Join(Stopped);

        // Assert.
        Assert.True(read, $"{entryPoint} never read a registration's type, so the guard proves nothing");
        Assert.True(answered, $"{entryPoint} held the model's lock while its read was stopped; a question from another thread waited {Patience.TotalSeconds} s for it");
        Assert.True(finished, $"{entryPoint} did not finish once its read was let go");
    }

    // A question that needs the model's lock and nothing else, asked from a thread of its own.
    private static bool AnswersFromAnotherThread(HierarchyModel model)
    {
        var asking = new Thread(() => model.TryResolvePath(ShortGuid.NewShortGuid(), out _, out _)) { IsBackground = true };
        asking.Start();
        return asking.Join(Patience);
    }

    private Action Arrange(string entryPoint, HierarchyModel model)
    {
        switch (entryPoint)
        {
            case "ListChildren":
            {
                Write("ideas.mm", "<map/>");
                return () => model.ListChildren(null);
            }
            case "Reconcile":
            {
                model.ListChildren(null);
                Write("ideas.mm", "<map/>");
                return model.Reconcile;
            }
            case "OnWatcherEvent":
            {
                Write("ideas.mm", "<map/>");
                model.ListChildren(null);
                var registration = Write("ideas.adp", Mindmap.Origin.MimeType + "\n");
                return () => model.OnWatcherEvent(WatcherChangeTypes.Created, null, registration);
            }
            case "ApplyLocalRename":
            {
                var oldPath = Write("ideas.mm", "<map/>");
                model.ListChildren(null);
                var newPath = IoPath.Combine(_root, "renamed.mm");
                File.Move(oldPath, newPath);
                return () => model.ApplyLocalRename(oldPath, newPath);
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, "No such entry point");
        }
    }

    private string Write(string relativePath, string content)
    {
        var path = IoPath.Combine(_root, relativePath);
        File.WriteAllText(path, content);
        return path;
    }
}
