using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.WardleyMap;

/// <summary>
/// Opens a <see cref="WardleySession"/> for one map. The fourth registration seam this module
/// uses, alongside its definition, its document factory and its context providers
/// (Requirement 10.8).
/// </summary>
public sealed class WardleySessionFactory : IDiagramSessionFactory
{
    private readonly IWardleyDocumentStore _documents;
    private readonly WardleyElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public WardleySessionFactory(
        IWardleyDocumentStore documents,
        WardleyElementMapper mapper,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin => Diagram.WardleyMap.Origin;

    /// <summary>
    /// A session for the map at <paramref name="bodyPath"/>. The registration file is ignored:
    /// a `wardley/map` `.adp` carries nothing beyond its MIME line, so there is no header to
    /// read - unlike C4, whose registration names which view it opens.
    /// </summary>
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = registrationPath;

        // The project's history, so a drag is one undo away like every other edit.
        return new WardleySession(bodyPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
