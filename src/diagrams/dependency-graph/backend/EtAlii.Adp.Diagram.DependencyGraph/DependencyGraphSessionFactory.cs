using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.DependencyGraph;

/// <summary>
/// Opens a <see cref="DependencyGraphSession"/> for one graph.
/// </summary>
public sealed class DependencyGraphSessionFactory : IDiagramSessionFactory
{
    private readonly IDependencyGraphDocumentStore _documents;
    private readonly DependencyGraphElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    /// <summary>Creates the factory for the origin this module declares.</summary>
    public DependencyGraphSessionFactory(
        IDependencyGraphDocumentStore documents,
        DependencyGraphElementMapper mapper,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.DependencyGraph.Origin;

    /// <summary>
    /// A session for the graph at <paramref name="bodyPath"/>. The registration file is ignored:
    /// a `generic/dependencies` `.adp` carries nothing beyond its MIME line, and a bare `.dgr`
    /// routes here with no registration at all.
    /// </summary>
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;

        // The project's history, so a drag is one undo away like every other edit.
        return new DependencyGraphSession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
