using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One connection's live view of one Ansible project. Holds nothing the folder holds - it draws the
/// definition's model of the open folder's reading (<see cref="AnsibleFolder.Disl"/>), built once per
/// reading - and adds only what is this connection's: its viewport, and the set of elements it has
/// already been sent.
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
/// <b>What this connection holds is kept by <see cref="DiagramDocumentChangeHandler"/></b>, as
/// every session's is (code-structure FR-031): the baseline, a viewport report and a change to the
/// folder all diff under its one lock against the one record of what was delivered.
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
    private readonly AnsibleFolders _folders;
    private readonly AnsibleFolder _opened;
    private readonly AnsibleElementMapper _mapper;

    /// <summary>The project's history, so a drag is one undo away. Null makes the diagram read-only.</summary>
    private readonly IHistoryStack? _history;

    /// <summary>The open folder's word for this folder, until the session goes.</summary>
    private readonly IDisposable _watch;

    private readonly DiagramDocumentChangeHandler _changes;
    private DiagramViewport _viewport = DiagramViewport.Unbounded;

    public AnsibleSession(
        string folder,
        AnsibleFolders folders,
        AnsibleElementMapper mapper,
        string? registrationPath = null,
        IHistoryStack? history = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(mapper);

        _folder = folder;
        _folders = folders;
        _mapper = mapper;
        _registrationPath = registrationPath;
        _history = history;
        _changes = new DiagramDocumentChangeHandler(_folder, Render, Raise);

        _opened = _folders.Acquire(_folder, _registrationPath);
        _watch = _folders.Watch(_folder, _registrationPath, _ => _changes.OnDocumentChanged(_folder));
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var elements = _changes.Deliver();
        return elements.Count == 0 ? [] : [new DiagramAddDelta(elements)];
    }

    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport)
    {
        _viewport = viewport;
        return _changes.Refresh();
    }

    /// <summary>
    /// Refused, always. An Ansible structure diagram is drawn from the folder's own files, and
    /// there is nowhere for a moved element to be moved <em>to</em> - the layout is derived and
    /// the files carry no positions.
    /// </summary>
    /// <remarks>
    /// Answered honestly rather than by throwing or by quietly succeeding: the seam returns a
    /// reason string precisely so a type that cannot do a thing can say so in the user's terms.
    /// The sentence is the definition's (<see cref="AnsibleDefinition.MoveRefusal"/>).
    /// </remarks>
    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken) =>
        Task.FromResult(AnsibleDefinition.MoveRefusal);

    /// <summary>
    /// Stores an authored position in the registration's <c>layout:</c> block, as one undoable
    /// command - and never writes a file inside the registered folder (Requirements 1.1, 2.1,
    /// 2.3).
    /// </summary>
    /// <remarks>
    /// The write comes back through the folder body, which watches the registration as the
    /// registration and raises a change without reading the folder: dragged and computed positions
    /// therefore share one render path, and this session needs no local echo of what it just asked
    /// for. A stored position whose node is absent is left in the registration, and is applied
    /// again when the node returns (finding B9).
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
        _watch.Dispose();
        _folders.Release(_folder, _registrationPath);
        return ValueTask.CompletedTask;
    }

    /// <summary>What this connection should hold: the model of the folder as it was last read, through its viewport.</summary>
    private IReadOnlyList<DiagramElement> Render() =>
        _mapper.Visible(_opened.Disl.Diagram, _viewport, Stored());

    private void Raise(IReadOnlyList<DiagramDelta> deltas) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(deltas));
}
