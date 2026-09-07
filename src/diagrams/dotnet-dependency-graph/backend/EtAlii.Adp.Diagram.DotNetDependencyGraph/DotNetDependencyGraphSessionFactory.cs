using EtAlii.Adp.Common;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Opens a <see cref="DotNetDependencyGraphSession"/> per connection, against the solution the
/// registration's <c>body:</c> header names.
/// </summary>
/// <remarks>
/// <b>The binding is stated, never inferred</b> (Requirement 2.3). Because this type declares
/// <c>.sln</c> and <c>.slnx</c> as document extensions, core routes the registration to its
/// body and hands that path over here - the <c>c4</c> pattern rather than the ansible one,
/// which takes the containing folder and therefore cannot say which solution when a folder
/// holds two.
/// <para>
/// It takes the history store like every other module's factory, for the module's single edit:
/// a reposition, dispatched as core's <c>SetRegistrationLayoutCommand</c>. The solution and its
/// project files stay read-only.
/// </para>
/// </remarks>
public sealed class DotNetDependencyGraphSessionFactory : IDiagramSessionFactory
{
    private readonly DependencyGraphStore _store;
    private readonly DependencyElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public DotNetDependencyGraphSessionFactory(
        DependencyGraphStore store,
        DependencyElementMapper mapper,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _store = store;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyPath);

        // The body IS the solution - that is what declaring the extensions buys, and it is why
        // this module needs no folder-walking of its own. The registration is where a position
        // is authored, and the project's history is what makes that write undoable.
        return new DotNetDependencyGraphSession(
            bodyPath,
            _store,
            _mapper,
            registrationPath,
            _historyStacks.Get(rootPath));
    }
}
