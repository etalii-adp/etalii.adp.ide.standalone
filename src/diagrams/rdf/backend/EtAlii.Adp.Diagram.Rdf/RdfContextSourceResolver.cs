using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Makes an RDF element selectable: resolves an <c>element_id</c> against the file named by the
/// enclosing level and verifies the id really is in it, on the resolver base (code-structure
/// FR-032). One is registered per reading, because the base answers for the files of one type;
/// what an element is does not depend on which reading a file is opened under, so all four
/// describe it the same way.
/// </summary>
/// <param name="origin">The reading whose files this resolver answers for.</param>
/// <param name="router">Routes a file to its diagram type.</param>
/// <param name="documents">The family's one store.</param>
/// <param name="drawn">What the reading draws for an id in a model, so a selection carries what the canvas was sent.</param>
public sealed class RdfContextSourceResolver(
    DiagramOrigin origin,
    DiagramFileRouter router,
    IRdfDocumentStore documents,
    Func<RdfModel, string, DiagramElement?> drawn)
    : DiagramElementSourceResolver<LineDocument, RdfModel>(router, documents)
{
    /// <inheritdoc />
    protected override DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    protected override DiagramElementTexts Texts { get; } = new(
        "The selected file is not an RDF document.",
        "That element is no longer in this file.",
        "New element",
        // A finished relation gesture names an element about to exist too, as a placement does.
        "New element");

    /// <inheritdoc />
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DocumentEntry<LineDocument, RdfModel> entry, string elementId)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var text = RdfSelection.Describe(entry, elementId)
            ?? OwlSelection.Describe(entry, elementId)
            ?? Shacl.ShaclSelection.Describe(entry, elementId);
        return text is null ? null : ([text], text);
    }

    /// <inheritdoc />
    protected override DiagramElement? Drawn(RdfModel model, string elementId) => drawn(model, elementId);
}
