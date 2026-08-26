using EtAlii.Adp.Backend.Diagrams;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Opens an <see cref="AnsibleSession"/> per connection - and resolves this type's one novelty:
/// the diagram's subject is the folder its registration sits in (Requirement 2.2).
/// </summary>
/// <remarks>
/// Note what is <em>not</em> injected. Every other module's session factory takes the project's
/// history store, because every other module's session can be edited through. This one takes a
/// store and a mapper, and that is all a read-only type needs.
/// </remarks>
public sealed class AnsibleSessionFactory : IDiagramSessionFactory
{
    private readonly IAnsibleProjectStore _store;
    private readonly AnsibleElementMapper _mapper;

    public AnsibleSessionFactory(IAnsibleProjectStore store, AnsibleElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);
        _store = store;
        _mapper = mapper;
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

        return new AnsibleSession(folder, _store, _mapper);
    }
}
