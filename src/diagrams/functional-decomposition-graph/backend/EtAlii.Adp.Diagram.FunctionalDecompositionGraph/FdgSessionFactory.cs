using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph;

/// <summary>Opens a session on an <c>.fdg</c> document.</summary>
/// <remarks>
/// No history stack yet: a session takes one only to execute commands, and the commands are task 12's.
/// </remarks>
public sealed class FdgSessionFactory : IDiagramSessionFactory
{
    private readonly IFdgDocumentStore _documents;
    private readonly FdgElementMapper _mapper;

    public FdgSessionFactory(IFdgDocumentStore documents, FdgElementMapper mapper)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        _documents = documents;
        _mapper = mapper;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.FunctionalDecompositionGraph.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = watchId;
        _ = rootPath;
        _ = registrationPath;
        return new FdgSession(bodyPath, _documents, _mapper);
    }
}
