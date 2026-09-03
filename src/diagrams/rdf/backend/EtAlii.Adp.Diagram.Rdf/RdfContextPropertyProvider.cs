using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The property rows of a selected RDF element, contributed as data the panel renders without
/// understanding (rdf-diagram Requirement 6). The editable rows - <c>rdfs:label</c> and
/// <c>rdfs:comment</c> - are exactly what <c>ReplaceObjectLiteral</c> exists for: a first write
/// states the triple, a later one rewrites its literal token in place.
/// </summary>
public sealed class RdfContextPropertyProvider : IContextPropertyProvider
{
    /// <summary>The resource's full IRI, read-only: renaming rewrites references, so the menu owns it.</summary>
    public const string IriProperty = "rdf.iri";

    /// <summary>The resource's types, read-only: types are stated by triples, edited as triples.</summary>
    public const string TypesProperty = "rdf.types";

    /// <summary>The resource's <c>rdfs:label</c>, editable.</summary>
    public const string LabelProperty = "rdf.label";

    /// <summary>The resource's <c>rdfs:comment</c>, editable.</summary>
    public const string CommentProperty = "rdf.comment";

    /// <summary>An edge's predicate, read-only.</summary>
    public const string PredicateProperty = "rdf.predicate";

    private const string IdentityGroup = "Identity";
    private const string DocumentationGroup = "Documentation";
    private const string RenameViaMenu = "Rename through the context menu, so every reference follows the name.";
    private const string TypesAreTriples = "Types are stated by triples; add or remove them as statements.";
    private const string BlankBoundary = "A blank node's identity does not survive a reparse, so nothing about it can be edited from the diagram.";

    private readonly IHistoryStackStore _historyStacks;
    private readonly IRdfDocumentStore _documents;

    public RdfContextPropertyProvider(IHistoryStackStore historyStacks, IRdfDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(historyStacks);
        ArgumentNullException.ThrowIfNull(documents);

        _historyStacks = historyStacks;
        _documents = documents;
    }

    /// <inheritdoc />
    public ContextScope Scope => ContextScope.DiagramElement;

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (!RdfSelection.CouldBeFamilyFile(target.ResolvedFullPath))
        {
            return Rows([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);

        // An element the file asserts to be a concept, scheme or collection gets the scheme
        // reading's grid wholesale; every other resource keeps the family's rows. Data-driven,
        // because the context seam does not carry which registration selected the element.
        if (SkosProperties.Describe(entry, target) is { } skosRows)
        {
            return Rows(skosRows);
        }

        if (RdfSelection.ResourceOf(entry, target.ElementId) is { } iri)
        {
            var truncated = RdfSelection.IsTruncated(entry);
            var types = entry.Model.Triples
                .Where(t => t.Subject is IriTerm s && s.Iri == iri && t.Predicate.Iri == RdfVocabulary.Type && t.Object is IriTerm)
                .Select(t => RdfProjection.Display(entry.Model, (IriTerm)t.Object));

            return Rows(
            [
                new ContextPropertyDefinition(IriProperty, "IRI", iri, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
                new ContextPropertyDefinition(TypesProperty, "Types", string.Join(", ", types), ReadOnlyReason: TypesAreTriples, Group: IdentityGroup),
                new ContextPropertyDefinition(
                    LabelProperty, "Label", LiteralOf(entry, iri, RdfVocabulary.Label),
                    ReadOnlyReason: truncated ? RdfSelection.TruncatedRefusal : "", Group: DocumentationGroup),
                new ContextPropertyDefinition(
                    CommentProperty, "Comment", LiteralOf(entry, iri, RdfVocabulary.Comment),
                    ReadOnlyReason: truncated ? RdfSelection.TruncatedRefusal : "", Group: DocumentationGroup),
            ]);
        }

        if (RdfSelection.EdgeOf(entry, target.ElementId) is { } edge)
        {
            return Rows(
            [
                new ContextPropertyDefinition(
                    PredicateProperty, "Predicate", RdfProjection.Display(entry.Model, edge.Predicate),
                    ReadOnlyReason: "A statement's predicate is the statement; remove and restate to change it.", Group: IdentityGroup),
            ]);
        }

        if (RdfSelection.IsBlank(target.ElementId) && RdfSelection.Describe(entry, target.ElementId ?? "") is { } text)
        {
            return Rows(
            [
                new ContextPropertyDefinition(IriProperty, "Node", text, ReadOnlyReason: BlankBoundary, Group: IdentityGroup),
            ]);
        }

        return Rows([]);
    }

    /// <inheritdoc />
    public async ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target,
        string propertyId,
        string value,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);
        if (RdfSelection.IsTruncated(entry))
        {
            return ContextPropertyResult.Failure(RdfSelection.TruncatedRefusal);
        }

        var command = CommandFor(entry, target, propertyId, value);
        if (command is null)
        {
            return ContextPropertyResult.Failure($"'{propertyId}' cannot be edited on this selection.");
        }

        // Through the project's history and out through the delta stream (Requirement 6.3).
        var result = await _historyStacks.Get(target.RootPath).ExecuteAsync(command, cancellationToken);
        return result.IsSuccess ? ContextPropertyResult.Success : ContextPropertyResult.Failure(result.Error);
    }

    private static ICommand? CommandFor(RdfDocumentEntry entry, ContextTarget target, string propertyId, string value)
    {
        if (SkosProperties.CommandFor(entry, target, propertyId, value) is { } skosCommand)
        {
            return skosCommand;
        }

        var predicateIri = propertyId switch
        {
            LabelProperty => RdfVocabulary.Label,
            CommentProperty => RdfVocabulary.Comment,
            _ => null,
        };
        if (predicateIri is null || RdfSelection.ResourceOf(entry, target.ElementId) is not { } iri)
        {
            return null;
        }

        // A first write states the triple; a later one rewrites exactly the literal token -
        // ReplaceObjectLiteral's whole purpose (the family writer's boundary-rule adoption).
        var existing = entry.Model.Triples.FirstOrDefault(t =>
            t.Subject is IriTerm s && s.Iri == iri && t.Predicate.Iri == predicateIri && t.Object is LiteralTerm);
        if (existing is null)
        {
            return new AddRdfTripleCommand(target.ResolvedFullPath, iri, predicateIri, "", value);
        }

        var literal = (LiteralTerm)existing.Object;
        return new ReplaceRdfObjectLiteralCommand(
            target.ResolvedFullPath, iri, predicateIri,
            literal.Lexical, literal.Language ?? "", literal.DatatypeIri ?? "",
            value, literal.Language ?? "", literal.DatatypeIri ?? "");
    }

    private static string LiteralOf(RdfDocumentEntry entry, string iri, string predicateIri) =>
        entry.Model.Triples
            .Where(t => t.Subject is IriTerm s && s.Iri == iri && t.Predicate.Iri == predicateIri)
            .Select(t => t.Object)
            .OfType<LiteralTerm>()
            .FirstOrDefault()?.Lexical ?? "";

    private static ValueTask<IReadOnlyList<ContextPropertyDefinition>> Rows(IReadOnlyList<ContextPropertyDefinition> rows) =>
        ValueTask.FromResult(rows);
}
