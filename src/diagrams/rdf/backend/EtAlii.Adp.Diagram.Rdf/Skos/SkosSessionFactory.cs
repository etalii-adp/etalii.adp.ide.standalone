using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Diagrams;


using EtAlii.Adp.Common;
namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Opens a <see cref="SkosSession"/> for the scheme reading, seeding the label chooser with the
/// registration's <c>language:</c> header where one sits in the band, and the default order
/// otherwise (skos-diagram Requirements 2, 3.2).
/// </summary>
public sealed class SkosSessionFactory : IDiagramSessionFactory
{
    private readonly IRdfDocumentStore _documents;
    private readonly SkosElementMapper _mapper;
    private readonly IHistoryStackStore _historyStacks;

    public SkosSessionFactory(
        DiagramOrigin origin,
        IRdfDocumentStore documents,
        SkosElementMapper mapper,
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

        // A misplaced header yields null here and a validation finding there; the session never
        // guesses (Requirement 3.2).
        var (language, _) = SkosRegistrationLanguage.Read(registrationPath);

        return new SkosSession(
            bodyPath,
            registrationPath,
            language ?? SkosLabels.DefaultLanguage,
            _documents,
            _mapper,
            _historyStacks.Get(rootPath));
    }
}
