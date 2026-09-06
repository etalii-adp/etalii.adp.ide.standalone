using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Backend.Hierarchy;


using EtAlii.Adp.Common;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One connection's live view of one Ansible project. Holds nothing the store holds - it reads
/// the shared <see cref="AnsibleProject"/> - and adds only what is this connection's: its
/// viewport, and the set of elements it has already been sent.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes the project's history, and uses it for exactly one thing.</b> The module still
/// owns no command of its own: a reposition dispatches core's
/// <see cref="SetRegistrationLayoutCommand"/>, so a drag is one undo away like every other
/// edit in ADP. A null stack is the read-only case and refuses in the user's terms.
/// </para>
/// <para>
/// <b>Nothing inside the registered folder is ever written.</b> Positions are metadata about
/// another ecosystem's files, so they go to the <c>.adp</c> and nowhere else - the
/// layout-in-.adp rule (Requirements 2.1, 2.3).
/// </para>
/// <para>
/// <b>The subject is the folder the <c>.adp</c> sits in.</b> For a type that declares no
/// document extension the router hands over the registration as its own body, so the folder is
/// one <c>GetDirectoryName</c> away - and the registration file itself is never parsed for
/// anything beyond the MIME line core already read.
/// </para>
/// </remarks>
internal sealed class AnsibleSession : IDiagramSession
{
    private readonly string _folder;
    private readonly string? _registrationPath;
    private readonly IAnsibleProjectStore _store;
    private readonly AnsibleElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    private DiagramViewport _viewport = DiagramViewport.Unbounded;
    private IReadOnlyList<DiagramElement> _delivered = [];

    public AnsibleSession(
        string folder,
        IAnsibleProjectStore store,
        AnsibleElementMapper mapper,
        string? registrationPath = null,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);

        _folder = folder;
        _store = store;
        _mapper = mapper;
        _registrationPath = registrationPath;
        _history = history;

        _store.Acquire(_folder);
        _store.Changed += OnProjectChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var project = _store.GetOrLoad(_folder);
        _delivered = _mapper.Visible(project, AnsibleGraph.Derive(project), _viewport, Stored());
        return _delivered.Count == 0 ? [] : [new DiagramAddDelta(_delivered)];
    }

    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        var project = _store.Get(_folder);
        if (project is null)
        {
            _viewport = viewport;
            return [];
        }

        _viewport = viewport;
        var after = _mapper.Visible(project, AnsibleGraph.Derive(project), _viewport, Stored());
        var deltas = _mapper.Diff(_delivered, after);
        _delivered = after;
        return deltas;
    }

    /// <summary>
    /// Refused, always. An Ansible structure diagram is drawn from the folder's own files, and
    /// there is nowhere for a moved element to be moved <em>to</em> - the layout is derived and
    /// the files carry no positions.
    /// </summary>
    /// <remarks>
    /// Answered honestly rather than by throwing or by quietly succeeding: the seam returns a
    /// reason string precisely so a type that cannot do a thing can say so in the user's terms.
    /// </remarks>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken) =>
        Task.FromResult(
            "An Ansible structure diagram is drawn from the folder's own files, so nothing on it can be moved from here. " +
            "Move a role by moving its folder.");

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes a file inside the registered folder (Requirements 1.1, 2.1,
    /// 2.3).
    /// </summary>
    /// <remarks>
    /// The write comes back through the folder's own watcher, because the <c>.adp</c> lives
    /// inside the folder being watched: dragged and computed positions therefore share one
    /// render path, and this session needs no local echo of what it just asked for.
    /// </remarks>
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

        if (elementId.StartsWith("edge:", StringComparison.Ordinal))
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
    /// The positions authored into the registration, or none when this diagram was opened
    /// without one. Re-read per render rather than cached: the file is the truth, and the
    /// watcher that brings a layout write back does not hand over its contents.
    /// </summary>
    private IReadOnlyDictionary<string, RegistrationPosition> Stored() =>
        _registrationPath is { Length: > 0 }
            ? RegistrationLayout.Read(_registrationPath)
            : new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

    public ValueTask DisposeAsync()
    {
        _store.Changed -= OnProjectChanged;
        _store.Release(_folder);
        return ValueTask.CompletedTask;
    }

    private void OnProjectChanged(object? sender, AnsibleProjectChangedEventArgs args)
    {
        if (Changed is null ||
            !string.Equals(IoPath.GetFullPath(args.FolderPath), IoPath.GetFullPath(_folder), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var after = _mapper.Visible(args.Project, AnsibleGraph.Derive(args.Project), _viewport, Stored());
        var deltas = _mapper.Diff(_delivered, after);
        _delivered = after;

        if (deltas.Count > 0)
        {
            Changed.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }
    }
}
