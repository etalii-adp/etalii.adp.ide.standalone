using EtAlii.Adp.Documents;
using EtAlii.Adp.History;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>Opens a session on a behavior model, with the project's history for its edits and moves.</summary>
public sealed class AbmSessionFactory : IDiagramSessionFactory
{
    private readonly IAbmDocumentStore _documents;
    private readonly AbmElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public AbmSessionFactory(IAbmDocumentStore documents, AbmElementMapper mapper, IHistoryStackStore historyStacks)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(historyStacks);
        _documents = documents;
        _mapper = mapper;
        _historyStacks = historyStacks;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        return new AbmSession(bodyPath, registrationPath, _documents, _mapper, _historyStacks.Get(rootPath));
    }
}
