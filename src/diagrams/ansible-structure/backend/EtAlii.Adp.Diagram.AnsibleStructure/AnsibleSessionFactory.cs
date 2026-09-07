using EtAlii.Adp.Common;
using EtAlii.Adp.History;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Opens an <see cref="AnsibleSession"/> per connection - and resolves this type's one novelty:
/// the diagram's subject is the folder its registration sits in (Requirement 2.2).
/// </summary>
/// <remarks>
/// It takes the history store like every other module's factory, for the module's single edit:
/// a reposition, dispatched as core's <c>SetRegistrationLayoutCommand</c>. The Ansible files
/// themselves stay read-only - the only thing this type ever writes is the <c>layout:</c> block
/// of its own registration.
/// </remarks>
public sealed class AnsibleSessionFactory : IDiagramSessionFactory
{
    private readonly IAnsibleProjectStore _store;
    private readonly AnsibleElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public AnsibleSessionFactory(IAnsibleProjectStore store, AnsibleElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _store = store;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin => Diagram.AnsibleStructure.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        // For a type that declares no document extension, the router routes the registration as
        // its own body - so bodyPath and registrationPath are the same file, and either names
        // the folder. Preferring the registration is deliberate all the same: it is the file
        // that made this folder a diagram, and if the two ever diverge that is the one to trust.
        var marker = registrationPath ?? bodyPath;
        var folder = IoPath.GetDirectoryName(IoPath.GetFullPath(marker))
                     ?? throw new InvalidOperationException($"'{marker}' has no folder to read.");

        // The marker is the registration: the file that made this folder a diagram, and the
        // file a position is authored into. The project's history makes that write undoable.
        return new AnsibleSession(folder, _store, _mapper, marker, _historyStacks.Get(rootPath));
    }
}
