using EtAlii.Adp.Backend.Context;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What one of this family's element ids names inside a loaded entry - the shared vocabulary the
/// resolver, the action provider and the property provider all answer from, so the three can
/// never disagree about what is selected. This is THE selection seam: sibling readings extend
/// these id shapes rather than inventing their own, and parse them here alone.
/// </summary>
/// <remarks>
/// The shapes: <c>res:{iri}</c> for an IRI-named resource, <c>blank:{ordinal}</c> for a blank
/// node (parse-local, per the identity boundary), and
/// <c>edge:{fromId}|{predicateIri}|{toId}</c> for a drawn triple - <c>|</c> is safe as the
/// separator because an IRI cannot carry it unencoded.
/// </remarks>
internal static class RdfSelection
{
    /// <summary>The truncation-withheld sentence every editing surface answers with (Requirement 8.4).</summary>
    public const string TruncatedRefusal =
        "The diagram shows only the first part of this file under the drawn-element budget, so edits through it are withheld - an edit through a partial view could touch what the view does not show. Edit the file as text instead.";

    /// <summary>Whether a path could be one of this family's bodies at all - a cheap first gate.</summary>
    public static bool CouldBeFamilyFile(string path) =>
        IoPath.GetExtension(path).ToLowerInvariant() is ".ttl" or ".nt";

    /// <summary>
    /// Whether this family's own providers should answer for <paramref name="target"/>: the
    /// origin decides where the resolver supplied one, and the extension decides where it did not.
    /// </summary>
    /// <remarks>
    /// The family answers for EVERY reading in the family - a rename or a remove-statement is as
    /// true of an ontology or a shapes graph as of a data graph - which is what makes it the
    /// family's provider rather than the data graph's. A sibling reading answers only for its own
    /// origin, so the two never shadow each other; where both answer, the reading's group is listed
    /// first and the family's beneath it (the family coordination ruling of 2026-09-04).
    /// <para>
    /// What this buys is delegation precision, not shortcut arbitration. The family provider
    /// hands a selection to the reading the REGISTRATION named, instead of inferring the reading
    /// from what the document happens to assert - so a vocabulary opened as a plain data graph
    /// stops offering scheme verbs. Keystrokes are a separate matter: this family's readings
    /// delegate inside one provider, and <c>ContextActionResolver</c> flattens a provider's own
    /// groups and takes the first match, so within it the group ORDER is the shortcut order.
    /// </para>
    /// <para>
    /// A null origin means the resolver has not adopted the field yet, never "no reading", so the
    /// answer falls back to what this provider did before the field existed.
    /// </para>
    /// </remarks>
    public static bool AnswersFor(ContextTarget target) =>
        target.Origin is { } origin
            ? origin.Vendor == ServiceCollectionAddRdfExtension.RdfOrigin.Vendor
            : CouldBeFamilyFile(target.ResolvedFullPath);

    /// <summary>The IRI a <c>res:</c> id names, when the model actually states it; null otherwise.</summary>
    public static string? ResourceOf(RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("res:", StringComparison.Ordinal))
        {
            return null;
        }

        var iri = elementId["res:".Length..];
        return entry.Model.Triples.Any(t => Names(t, iri)) ? iri : null;
    }

    /// <summary>Whether the id names a blank node - selectable, describable, edit-refused.</summary>
    public static bool IsBlank(string? elementId) =>
        elementId is not null && elementId.StartsWith("blank:", StringComparison.Ordinal);

    /// <summary>The drawn triple an <c>edge:</c> id names, when the model still states it.</summary>
    public static RdfTriple? EdgeOf(RdfDocumentEntry entry, string? elementId)
    {
        if (elementId is null || !elementId.StartsWith("edge:", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = elementId["edge:".Length..].Split('|');
        if (parts.Length < 3 || !parts[0].StartsWith("res:", StringComparison.Ordinal) || !parts[2].StartsWith("res:", StringComparison.Ordinal))
        {
            // Blank-ended edges select for describing, but carry no editable triple identity.
            return null;
        }

        var fromIri = parts[0]["res:".Length..];
        var predicateIri = parts[1];
        var toIri = parts[2]["res:".Length..];
        return entry.Model.Triples.FirstOrDefault(t =>
            t.Subject is IriTerm s && s.Iri == fromIri
            && t.Predicate.Iri == predicateIri
            && t.Object is IriTerm o && o.Iri == toIri);
    }

    /// <summary>Every triple the IRI is subject or object of - what a removal takes with it (Requirement 6).</summary>
    public static IReadOnlyList<RdfTriple> Touching(RdfDocumentEntry entry, string iri) =>
        entry.Model.Triples
            .Where(t => Names(t, iri))
            .ToList();

    /// <summary>Whether the drawn view is budget-cut, in which case every edit is withheld.</summary>
    public static bool IsTruncated(RdfDocumentEntry entry) =>
        RdfProjection.Project(entry.Model).Truncated;

    /// <summary>
    /// The display text an id resolves to, or null when the entry holds nothing by that id -
    /// which is what tells the resolver another module's element is being asked about.
    /// </summary>
    public static string? Describe(RdfDocumentEntry entry, string elementId)
    {
        if (ResourceOf(entry, elementId) is { } iri)
        {
            return RdfProjection.Display(entry.Model, new IriTerm(iri, ""));
        }

        if (elementId.StartsWith("blank:", StringComparison.Ordinal)
            && int.TryParse(elementId["blank:".Length..], out var ordinal))
        {
            var blank = entry.Model.Triples
                .Select(t => t.Subject)
                .OfType<BlankTerm>()
                .FirstOrDefault(b => b.Ordinal == ordinal);
            return blank is null ? null : blank.Label is { Length: > 0 } label ? $"_:{label}" : $"_:b{blank.Ordinal}";
        }

        if (EdgeOf(entry, elementId) is { } edge)
        {
            var from = RdfProjection.Display(entry.Model, (IriTerm)edge.Subject);
            var to = RdfProjection.Display(entry.Model, (IriTerm)edge.Object);
            return $"{from} → {to}";
        }

        if (elementId == RdfElementMapper.TruncationId && IsTruncated(entry))
        {
            return "Truncated view";
        }

        return null;
    }

    private static bool Names(RdfTriple triple, string iri) =>
        (triple.Subject is IriTerm s && s.Iri == iri) || (triple.Object is IriTerm o && o.Iri == iri);
}
