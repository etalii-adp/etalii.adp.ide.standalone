using EtAlii.Adp.Backend;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram;
namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Opens a <see cref="DatabricksSession"/> for one of the family's three diagram types. Three
/// of these are registered over the one shared store, and they differ only in the origin they
/// answer to - which declaration a session shows comes from the <c>.adp</c>'s <c>resource:</c>
/// header, not from which factory opened it (the C4 view-key precedent).
/// </summary>
public sealed class DatabricksSessionFactory : IDiagramSessionFactory
{
    private readonly IDatabricksDocumentStore _documents;
    private readonly DatabricksElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public DatabricksSessionFactory(
        DiagramOrigin origin,
        IDatabricksDocumentStore documents,
        DatabricksElementMapper mapper,
        IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);

        Origin = origin;
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    public DiagramOrigin Origin { get; }

    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;

        return new DatabricksSession(
            bodyPath,
            registrationPath,
            Origin,
            DatabricksHeaders.ReadResourceKey(registrationPath),
            _documents,
            _mapper,
            // The project's history, so a drag on this canvas is one undo away like every other edit.
            _historyStacks.Get(rootPath));
    }
}
