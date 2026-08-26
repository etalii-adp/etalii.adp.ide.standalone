using EtAlii.Adp.Backend.Diagrams;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One connection's live view of one Ansible project. Holds nothing the store holds - it reads
/// the shared <see cref="AnsibleProject"/> - and adds only what is this connection's: its
/// viewport, and the set of elements it has already been sent.
/// </summary>
/// <remarks>
/// <para>
/// <b>It takes no <c>IHistoryStack</c>.</b> Not "takes one and never uses it": a module with no
/// commands has no reason to hold the project's history, and the missing constructor parameter
/// is the clearest statement this design makes.
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
    private readonly IAnsibleProjectStore _store;
    private readonly AnsibleElementMapper _mapper;

    private DiagramViewport _viewport = DiagramViewport.Unbounded;
    private IReadOnlyList<DiagramElement> _delivered = [];

    public AnsibleSession(string folder, IAnsibleProjectStore store, AnsibleElementMapper mapper)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);

        _folder = folder;
        _store = store;
        _mapper = mapper;

        _store.Acquire(_folder);
        _store.Changed += OnProjectChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline()
    {
        var project = _store.GetOrLoad(_folder);
        _delivered = _mapper.Visible(project, AnsibleGraph.Derive(project), _viewport);
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
        var after = _mapper.Visible(project, AnsibleGraph.Derive(project), _viewport);
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

        var after = _mapper.Visible(args.Project, AnsibleGraph.Derive(args.Project), _viewport);
        var deltas = _mapper.Diff(_delivered, after);
        _delivered = after;

        if (deltas.Count > 0)
        {
            Changed.Invoke(this, new DiagramDeltasEventArgs(deltas));
        }
    }
}
