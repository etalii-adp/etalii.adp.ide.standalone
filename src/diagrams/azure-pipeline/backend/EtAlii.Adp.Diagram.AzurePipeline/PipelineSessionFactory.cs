using EtAlii.Adp.Backend.Diagrams;

namespace EtAlii.Adp.Diagram.AzurePipeline;

/// <summary>
/// Opens a <see cref="PipelineSession"/> for the one pipeline diagram type.
/// </summary>
/// <remarks>
/// The registration file is not read. A C4 diagram's <c>.adp</c> names which view of the workspace
/// to show, so C4's factory has a header to parse; a pipeline file is one pipeline and there is
/// nothing to choose between. It is also routinely opened with no registration at all - Add-on-a
/// -file is how a <c>.yml</c> already in the repository becomes a diagram (Requirement 2), and
/// that path never writes one.
/// </remarks>
public sealed class PipelineSessionFactory : IDiagramSessionFactory
{
    private readonly IPipelineDocumentStore _documents;
    private readonly PipelineElementMapper _mapper;
    private readonly PipelineViewState _views;

    /// <summary>Creates the factory for the origin this module declares.</summary>
    public PipelineSessionFactory(
        IPipelineDocumentStore documents,
        PipelineElementMapper mapper,
        PipelineViewState views)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(views);

        _documents = documents;
        _mapper = mapper;
        _views = views;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.Pipeline.Origin;

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath)
    {
        _ = registrationPath;
        return new PipelineSession(watchId, rootPath, bodyPath, _documents, _mapper, _views);
    }
}
