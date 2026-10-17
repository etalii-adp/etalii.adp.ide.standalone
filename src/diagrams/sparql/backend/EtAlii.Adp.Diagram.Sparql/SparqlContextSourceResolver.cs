using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>
/// Makes a query element selectable: resolves an <c>element_id</c> against the file named by the
/// enclosing level and verifies the id really is in it.
/// </summary>
/// <remarks>
/// <b>It describes the whole entry rather than its model</b>, because the header of a query that
/// does not parse reads as a plain "Query": its empty model would otherwise call it a SELECT.
/// </remarks>
public sealed class SparqlContextSourceResolver(DiagramFileRouter router, ISparqlDocumentStore documents, SparqlElementMapper mapper)
    : DiagramElementSourceResolver<string, SparqlQueryModel>(router, documents)
{
    private readonly SparqlElementMapper _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

    /// <summary>No stored positions: a selection's payload says what an element is, not where it was dragged.</summary>
    private static readonly IReadOnlyDictionary<string, RegistrationPosition> _computed =
        new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal);

    /// <inheritdoc />
    protected override ToolOrigin Origin => ServiceCollectionAddSparqlExtension.SparqlOrigin;

    /// <inheritdoc />
    /// <remarks>Nothing is created or connected on a query diagram; the two names are for gestures it never offers.</remarks>
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not a SPARQL query.",
        "That element is no longer in this query.",
        "New element",
        "New relation");

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DocumentEntry<string, SparqlQueryModel> entry, string elementId) =>
        SparqlSelection.Describe(entry, elementId) is { } text
            ? ([text], text)
            : null;

    /// <inheritdoc />
    protected override DiagramElement? Drawn(SparqlQueryModel model, string elementId)
    {
        var projection = SparqlProjection.Project(model);
        return _mapper.Visible(projection, SparqlLayout.Compute(projection, _computed), DiagramViewport.Unbounded)
            .FirstOrDefault(candidate => candidate.Id == elementId);
    }
}
