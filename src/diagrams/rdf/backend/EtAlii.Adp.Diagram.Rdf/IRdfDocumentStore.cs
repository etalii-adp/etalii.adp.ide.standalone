using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The one owner of RDF documents on disk: one per open file, keyed by path, so every reading of
/// the family - the data graph and the sibling readings joining it - shares a file's document and
/// an edit made through one is visible in the others.
/// </summary>
/// <remarks>
/// <para>
/// This is the triplestore document store the family's specifications name: a file is parsed once
/// into triples plus a line-level concrete syntax, and siblings add projections over the model,
/// never parsers of their own. Turtle and N-Triples come through the same door - N-Triples is a
/// subset - and everything else is refused upstream at the definition, not here.
/// </para>
/// <para>
/// <b>The shared store's contract, under the family's name</b> (code-structure FR-029). The name stays
/// so the four readings, the commands and the restore edit can ask for this family's store by type:
/// one instance serves all four origins.
/// </para>
/// </remarks>
public interface IRdfDocumentStore : IDiagramDocumentStore<LineDocument, RdfModel>;
