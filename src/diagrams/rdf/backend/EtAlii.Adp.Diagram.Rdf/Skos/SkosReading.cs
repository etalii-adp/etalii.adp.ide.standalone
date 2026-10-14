using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The scheme reading: the projection, the chosen labels and the layered layout composed over the
/// family store, with the authored <c>layout:</c> overlay on top (skos-diagram Requirements 1-4).
/// </summary>
public sealed class SkosReading : IRdfReading<SkosProjectionResult>
{
    /// <summary>
    /// The cell an element occupies for the purpose of deciding whether it is on screen - the
    /// layout's own column and row pitch. Over-inclusion is the safe direction: one extra
    /// element on the wire against a hole the reader looks straight at.
    /// </summary>
    private const double CellWidth = 240;

    /// <inheritdoc cref="CellWidth" />
    private const double CellHeight = 110;

    private readonly SkosElementMapper _mapper;

    /// <param name="mapper">The scheme reading's mapper.</param>
    /// <param name="displayLanguage">The language the registration's header names, or the default where it names none.</param>
    public SkosReading(SkosElementMapper mapper, string displayLanguage)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayLanguage);
        _mapper = mapper;
        DisplayLanguage = displayLanguage.ToLowerInvariant();
    }

    /// <summary>The language the chooser was seeded with - what the canvas compares chips against.</summary>
    public string DisplayLanguage { get; }

    /// <inheritdoc />
    public string ParentRefusal => "A concept is filed under a parent with the hierarchy gesture; dragging changes where it sits on the canvas.";

    /// <inheritdoc />
    public SkosProjectionResult Project(RdfModel model) => SkosProjection.Project(model, int.MaxValue);

    /// <inheritdoc />
    /// <remarks>The hierarchy is laid out by walking it, so a layout of the visible set would rearrange as the reader moved.</remarks>
    public IReadOnlyDictionary<string, RegistrationPosition> Positions(
        SkosProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> stored) =>
        RegistrationLayout.Apply(SkosLayout.Layout(projection).Positions, stored);

    /// <inheritdoc />
    /// <remarks>
    /// The counts handed on are the document's, not this view's, because
    /// <see cref="SkosProjectionResult.Truncated" /> drives the banner and the read-only refusal
    /// and both are decided from the document with no viewport to consult.
    /// </remarks>
    public SkosProjectionResult InView(
        SkosProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions, DiagramViewport viewport)
    {
        ArgumentNullException.ThrowIfNull(projection);

        var schemes = projection.Schemes.Where(scheme => Visible(scheme.Id)).ToList();
        var concepts = projection.Concepts.Where(concept => Visible(concept.Id)).ToList();
        var collections = projection.Collections.Where(collection => Visible(collection.Id)).ToList();

        var truncated = projection.Total > RdfProjection.DefaultBudget;
        var drawn = schemes.Count + concepts.Count + collections.Count;

        var keptIds = schemes.Select(scheme => scheme.Id)
            .Concat(concepts.Select(concept => concept.Id))
            .Concat(collections.Select(collection => collection.Id))
            .ToHashSet(StringComparer.Ordinal);
        var edges = projection.Edges
            .Where(edge => keptIds.Contains(edge.FromId) && keptIds.Contains(edge.ToId))
            .ToList();

        return new SkosProjectionResult(
            schemes,
            concepts,
            collections,
            edges,
            projection.OutOfFileMappings,
            truncated ? RdfProjection.DefaultBudget : drawn,
            truncated ? projection.Total : drawn);

        bool Visible(string id) => RdfViewport.Admits(viewport, positions, id, CellWidth, CellHeight);
    }

    /// <inheritdoc />
    public IReadOnlyList<DiagramElement> Elements(
        SkosProjectionResult projection, IReadOnlyDictionary<string, RegistrationPosition> positions) =>
        _mapper.Elements(projection, positions, DisplayLanguage);

    /// <inheritdoc />
    public string? Refusal(string elementId)
    {
        ArgumentNullException.ThrowIfNull(elementId);

        if (elementId.StartsWith("edge:", StringComparison.Ordinal) || elementId == RdfElementMapper.TruncationId)
        {
            return "That element is not something this diagram can move.";
        }

        return elementId.StartsWith("blank:", StringComparison.Ordinal)
            ? "That is a blank node, whose identity does not survive a reparse, so a stored position could not be trusted. Name it with an IRI to arrange it."
            : null;
    }
}
