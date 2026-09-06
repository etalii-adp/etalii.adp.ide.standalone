using EtAlii.Adp.Backend;
using EtAlii.Adp.Backend.Context;
using EtAlii.Adp.Common;
using EtAlii.Adp.Diagram.Rdf.Shacl;

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

        if (!RdfSelection.AnswersFor(target))
        {
            return Rows([]);
        }

        var entry = _documents.GetOrLoad(target.ResolvedFullPath);

        // An element the file asserts to be a concept, scheme or collection gets the scheme
        // reading's grid wholesale; every other resource keeps the family's rows - the ontology
        // reading's individuals among them. Data-driven, because the context seam does not carry
        // which registration selected the element.
        if (SkosProperties.Describe(entry, target) is { } skosRows)
        {
            return Rows(skosRows);
        }

        // The shapes reading answers only for its own origin, so this cannot hijack the grid of
        // another reading over the same file - and it comes before the resource fallback because
        // a shape card's element id is an ordinary `res:` id that the fallback would also claim.
        if (ShaclProperties.Describe(entry, target) is { } shaclRows)
        {
            return Rows(shaclRows);
        }

        if ((RdfSelection.ResourceOf(entry, target.ElementId) ?? OwlSelection.IndividualIriOf(entry, target.ElementId)) is { } iri)
        {
            var truncated = RdfSelection.IsTruncated(entry);
            var types = entry.Model.Triples
                .Where(t => t.Subject is IriTerm s && s.Iri == iri && t.Predicate.Iri == RdfVocabulary.Type && t.Object is IriTerm)
                .Select(t => RdfProjection.Display(entry.Model, (IriTerm)t.Object));

            var rows = new List<ContextPropertyDefinition>
            {
                new(IriProperty, "IRI", iri, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
                new(TypesProperty, "Types", string.Join(", ", types), ReadOnlyReason: TypesAreTriples, Group: IdentityGroup),
                new(
                    LabelProperty, "Label", LiteralOf(entry, iri, RdfVocabulary.Label),
                    ReadOnlyReason: truncated ? RdfSelection.TruncatedRefusal : "", Group: DocumentationGroup),
                new(
                    CommentProperty, "Comment", LiteralOf(entry, iri, RdfVocabulary.Comment),
                    ReadOnlyReason: truncated ? RdfSelection.TruncatedRefusal : "", Group: DocumentationGroup),
            };
            rows.AddRange(OwlRowsFor(entry, iri));
            return Rows(rows);
        }

        // An expression node: the full, uncapped Manchester rendering, read-only with the
        // boundary's sentence as the stated reason (owl-diagram Requirements 3.2, 7.2).
        if (OwlSelection.ExpressionOf(entry, target.ElementId) is { ExpressionRoot: { } root })
        {
            var uncapped = ExpressionRenderer.Render(root, entry.Model);
            return Rows(
            [
                new ContextPropertyDefinition(
                    "owl.expression", "Expression", uncapped.Text,
                    ReadOnlyReason: OwlSelection.ExpressionRefusal, Group: IdentityGroup),
            ]);
        }

        // A derived property edge - a domain-range span, not one triple - shows the property's
        // own axioms (owl-diagram Requirement 7.3).
        if (OwlSelection.PropertyEdgeIriOf(entry, target.ElementId) is { } propertyIri)
        {
            return Rows(PropertyRowsFor(entry, propertyIri));
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

        // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
        // Reason: Can still be null if the target element ID is empty.
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

        if (ShaclProperties.CommandFor(entry, target, propertyId, value) is { } shaclCommand)
        {
            return shaclCommand;
        }

        var predicateIri = propertyId switch
        {
            LabelProperty => RdfVocabulary.Label,
            CommentProperty => RdfVocabulary.Comment,
            _ => null,
        };
        if (predicateIri is null
            || (RdfSelection.ResourceOf(entry, target.ElementId) ?? OwlSelection.IndividualIriOf(entry, target.ElementId)) is not { } iri)
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

    /// <summary>
    /// The ontology rows a named term carries beyond the family's: asserted superclasses,
    /// equivalents and disjoints on a class - expressions rendered uncapped, Manchester-style -
    /// and a property's own axioms. Empty for a term with no OWL axioms, so a plain data graph's
    /// grid stays exactly the family's (owl-diagram Requirements 7.2, 7.3).
    /// </summary>
    private static IEnumerable<ContextPropertyDefinition> OwlRowsFor(RdfDocumentEntry entry, string iri)
    {
        const string axiomGroup = "Axioms";
        const string axiomsAreTriples = "Axioms are stated by triples; draw or remove them as edges and statements.";
        var index = 0;
        foreach (var triple in entry.Model.Triples)
        {
            if (triple.Subject is not IriTerm subject || subject.Iri != iri)
            {
                continue;
            }

            var name = triple.Predicate.Iri switch
            {
                OwlVocabulary.SubClassOf => "Subclass of",
                OwlVocabulary.EquivalentClass => "Equivalent to",
                OwlVocabulary.DisjointWith => "Disjoint with",
                OwlVocabulary.Domain => "Domain",
                OwlVocabulary.Range => "Range",
                OwlVocabulary.InverseOf => "Inverse of",
                _ => null,
            };
            if (name is null)
            {
                continue;
            }

            var (value, reason) = triple.Object switch
            {
                IriTerm named => (RdfProjection.Display(entry.Model, named), axiomsAreTriples),
                // An expression axiom shows its full Manchester form; the boundary is the reason
                // it reads rather than edits (Requirements 3.2, 7.2).
                BlankTerm root => (ExpressionRenderer.Render(root, entry.Model).Text, OwlSelection.ExpressionRefusal),
                _ => ("", axiomsAreTriples),
            };
            yield return new ContextPropertyDefinition($"owl.axiom.{index}", name, value, ReadOnlyReason: reason, Group: axiomGroup);
            index++;
        }

        foreach (var word in CharacteristicsOf(entry, iri))
        {
            yield return new ContextPropertyDefinition($"owl.characteristic.{index}", "Characteristic", word, ReadOnlyReason: axiomsAreTriples, Group: axiomGroup);
            index++;
        }
    }

    /// <summary>A derived property edge's rows: the property's identity and its axioms.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> PropertyRowsFor(RdfDocumentEntry entry, string propertyIri)
    {
        var rows = new List<ContextPropertyDefinition>
        {
            new(IriProperty, "IRI", propertyIri, ReadOnlyReason: RenameViaMenu, Group: IdentityGroup),
        };
        rows.AddRange(OwlRowsFor(entry, propertyIri));
        return rows;
    }

    private static IEnumerable<string> CharacteristicsOf(RdfDocumentEntry entry, string iri)
    {
        foreach (var triple in entry.Model.Triples)
        {
            if (triple.Subject is IriTerm s && s.Iri == iri
                && triple.Predicate.Iri == RdfVocabulary.Type
                && triple.Object is IriTerm type)
            {
                var word = type.Iri switch
                {
                    OwlVocabulary.FunctionalProperty => "functional",
                    OwlVocabulary.InverseFunctionalProperty => "inverse functional",
                    OwlVocabulary.TransitiveProperty => "transitive",
                    OwlVocabulary.SymmetricProperty => "symmetric",
                    OwlVocabulary.AsymmetricProperty => "asymmetric",
                    OwlVocabulary.ReflexiveProperty => "reflexive",
                    OwlVocabulary.IrreflexiveProperty => "irreflexive",
                    _ => null,
                };
                if (word is not null)
                {
                    yield return word;
                }
            }
        }
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
