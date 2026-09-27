using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// One connection's live view of one solution's dependency graph.
/// </summary>
/// <remarks>
/// <para>
/// <b>The subject is a solution named by the registration's <c>body:</c> header</b>, which is
/// the one place this module departs from the ansible template it otherwise follows: a bodyless
/// registration takes its containing folder as the subject and cannot say <em>which</em>
/// solution when a folder holds two (Requirement 2.3).
/// </para>
/// <para>
/// <b>The only thing this session writes is a position</b>, into the registration's
/// <c>layout:</c> block, as core's <see cref="SetRegistrationLayoutCommand"/> - so a drag is one
/// undo away like every other edit in ADP, and the module owns no command of its own
/// (Requirement 6.6). No <c>.csproj</c>, <c>.sln</c> or <c>.slnx</c> is ever written.
/// </para>
/// <para>
/// <b>Elements are draggable although the subject is read-only</b> (Requirement 6.5):
/// arrangement is a view concern, not an edit to the solution.
/// </para>
/// </remarks>
internal sealed class DotNetDependencyGraphSession : IDiagramSession
{
    private readonly string _solutionPath;
    private readonly string? _registrationPath;
    private readonly DependencyGraphStore _store;
    private readonly DependencyElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>
    /// Watches the files this graph was derived from, so a change on disk pushes a refresh
    /// (Requirement 7.1). Null when watching was declined - which the tests do, so that a test
    /// asserting a refresh is asserting the refresh rather than the file system's timing.
    /// </summary>
    private readonly SolutionWatcher? _watcher;

    private IReadOnlyList<DiagramElement> _delivered = [];

    /// <summary>Guards <see cref="_delivered"/>; see <see cref="Rediff"/>.</summary>
    private readonly Lock _deliveredGate = new();

    public DotNetDependencyGraphSession(
        string solutionPath,
        DependencyGraphStore store,
        DependencyElementMapper mapper,
        string? registrationPath = null,
        IHistoryStack? history = null,
        bool watch = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);

        _solutionPath = solutionPath;
        _store = store;
        _mapper = mapper;
        _registrationPath = registrationPath;
        _history = history;

        if (watch)
        {
            // Automatic, because the product capability is live pushed updates and every other
            // module behaves that way.
            _watcher = new SolutionWatcher(_store.WatchedFiles(solutionPath));
            _watcher.Stale += OnSolutionStale;
        }

        // A position change arrives through the history, never through the watcher - see
        // OnHistoryChanged. Not conditional on `watch`: this is the answer to a user's own
        // gesture, not a notice about files changing underneath the diagram.
        _history?.Changed += OnHistoryChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        lock (_deliveredGate)
        {
            _delivered = _mapper.Elements(_store.GetOrLoad(_solutionPath), Stored());
            return _delivered.Count == 0 ? [] : [new DiagramAddDelta(_delivered)];
        }
    }

    /// <summary>
    /// The whole graph, whatever the viewport. Deliberate, and the honest state of this module
    /// at task 8: the graph is small enough for every solution measured so far, and viewport
    /// culling is a scale answer rather than a rendering one - task 13 measures this repository's
    /// own solution and decides what "too large to read" requires.
    /// </summary>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport) => Rediff(_store.GetOrLoad(_solutionPath));

    /// <summary>
    /// Renders the graph with the positions the registration holds now, and returns what differs
    /// from what was last delivered - recording the new rendering as delivered.
    /// </summary>
    /// <remarks>
    /// One place, under one lock, because three callers write <see cref="_delivered"/> from three
    /// threads: a view update on a request, a refresh on the watcher's, and a history change on
    /// whichever request ran the command. Unlocked, two of them interleaving could each diff
    /// against the other's stale "before" and one push would be lost - which, for a move, is
    /// exactly the defect this class was just fixed for. The event is raised by the caller,
    /// OUTSIDE the lock, as <c>HistoryStack</c> does: a subscriber that re-enters cannot deadlock.
    /// </remarks>
    private IReadOnlyList<DiagramDelta> Rediff(DependencyGraphModel graph)
    {
        lock (_deliveredGate)
        {
            var after = _mapper.Elements(graph, Stored());
            var deltas = DiagramDiff.Between(_delivered, after);
            _delivered = after;
            return deltas;
        }
    }

    /// <summary>
    /// A command ran, or was undone or redone, somewhere in this project - so push whatever the
    /// stored positions now say.
    /// </summary>
    /// <remarks>
    /// <b>This is what a drag was missing.</b> A move writes the position into the registration
    /// and nothing more; the watcher covers the solution and its projects but not the
    /// registration, so no delta followed, and the element stayed where it was dropped FROM until
    /// a zoom asked <see cref="UpdateView"/> for the same diff this pushes. Undo and redo of a
    /// move had the identical gap, which is why this listens to the history rather than being a
    /// line at the end of <see cref="MoveElementToAsync"/>: the history is the one thing that
    /// sees all three.
    /// <para>
    /// The history is the PROJECT's, so this also runs when another diagram's command lands.
    /// That costs a render against the cached graph and a diff that comes back empty - nothing is
    /// re-parsed and nothing is pushed - which is cheaper than a second, narrower mechanism that
    /// could miss one of the three.
    /// </para>
    /// </remarks>
    private void OnHistoryChanged(object? sender, EventArgs args)
    {
        var deltas = Rediff(_store.GetOrLoad(_solutionPath));
        if (deltas.Count > 0)
        {
            Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }
    }

    /// <summary>
    /// Refused, always. The graph's shape is the solution's, and nothing on this canvas can be
    /// reparented - a project belongs to the solution that names it.
    /// </summary>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken) =>
        Task.FromResult(
            "A .NET dependency graph is drawn from the solution's own files, so nothing on it can be moved from here. " +
            "Change a reference in the project file, and the diagram will follow.");

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes a file the build owns (Requirements 6.1, 6.5, 6.6).
    /// </summary>
    public async Task<string> MoveElementToAsync(string elementId, double x, double y, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        cancellationToken.ThrowIfCancellationRequested();

        if (_history is null)
        {
            return "This diagram is read-only.";
        }

        if (_registrationPath is not { Length: > 0 })
        {
            return "This diagram was opened without a registration, so there is nowhere to store a position.";
        }

        if (elementId.StartsWith("depends:", StringComparison.Ordinal))
        {
            // An edge has no position of its own - it follows its endpoints.
            return "That element is not something this diagram can move.";
        }

        var result = await _history.ExecuteAsync(
            new SetRegistrationLayoutCommand(_registrationPath, elementId, x, y),
            cancellationToken);

        return result.IsSuccess ? "" : result.Error;
    }

    /// <summary>
    /// Recomputes from the files and pushes what changed - the refresh of Requirement 7.
    /// Stored positions are re-applied by <see cref="Stored"/> on the way out, so a refresh
    /// never costs the user their arrangement (Requirement 7.2), and an element that has
    /// vanished from the recomputed graph is removed (Requirement 7.3).
    /// </summary>
    /// <remarks>
    /// <b>Explicit as well as automatic, for a reason the cache-only decision created</b>
    /// (Requirement 7.4). A watcher covers every file the graph was derived from, but a package
    /// description becomes available after a <c>restore</c> writes into the machine's NuGet
    /// cache - which is not a watched file and is not in the workspace at all. Without an
    /// explicit refresh a user who restored a package would have no way to make its description
    /// appear short of reopening the diagram. The cache-only decision therefore reaches past the
    /// property it was about, and this method is where that shows.
    /// </remarks>
    public void Refresh()
    {
        var deltas = Rediff(_store.Reload(_solutionPath));

        if (deltas.Count > 0)
        {
            Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }
    }

    /// <summary>
    /// The positions authored into the registration, or none when this diagram was opened
    /// without one. Re-read per render rather than cached: the file is the truth.
    /// </summary>
    private IReadOnlyDictionary<string, RegistrationPosition> Stored() =>
        _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

    /// <summary>The watcher's own signal: the files moved, so recompute and push.</summary>
    private void OnSolutionStale(object? sender, EventArgs args) => Refresh();

    public ValueTask DisposeAsync()
    {
        if (_watcher is not null)
        {
            _watcher.Stale -= OnSolutionStale;
            _watcher.Dispose();
        }

        // The history outlives this session - it is the project's - so an unremoved handler would
        // keep a closed diagram alive and re-rendering on every command anyone runs.
        _history?.Changed -= OnHistoryChanged;

        return ValueTask.CompletedTask;
    }
}
