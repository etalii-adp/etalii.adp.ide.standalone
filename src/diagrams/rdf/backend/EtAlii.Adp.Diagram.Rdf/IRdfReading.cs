namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// What one reading of the family draws of a document, and what it refuses to arrange: the part of
/// a session that differs between the data graph, the ontology, the scheme and the shapes reading
/// (code-structure FR-038). <see cref="RdfFamilySession{TProjection}"/> is the rest, written once.
/// </summary>
/// <typeparam name="TProjection">What the reading projects a document's model into.</typeparam>
public interface IRdfReading<TProjection>
{
    /// <summary>What a drag onto another parent answers: in this reading's words, why nothing nests.</summary>
    string ParentRefusal { get; }

    /// <summary>The whole of <paramref name="model"/>, projected without a budget, so everything the reader can pan to has a place.</summary>
    TProjection Project(RdfModel model);

    /// <summary>Where every element of <paramref name="projection"/> sits: the reading's layout, with the registration's <paramref name="stored"/> positions on top.</summary>
    IReadOnlyDictionary<string, RegistrationPosition> Positions(TProjection projection, IReadOnlyDictionary<string, RegistrationPosition> stored);

    /// <summary>
    /// What <paramref name="viewport"/> admits of <paramref name="projection"/>: the elements it
    /// intersects, the edges both of whose ends survived, and the budget applied to that set. The
    /// counts stay the document's, because what a document may do does not depend on the view.
    /// </summary>
    TProjection InView(TProjection projection, IReadOnlyDictionary<string, RegistrationPosition> positions, DiagramViewport viewport);

    /// <summary>The elements the connection is sent for <paramref name="projection"/>.</summary>
    IReadOnlyList<DiagramElement> Elements(TProjection projection, IReadOnlyDictionary<string, RegistrationPosition> positions);

    /// <summary>Why <paramref name="elementId"/> cannot be given a stored position, or null when it can.</summary>
    string? Refusal(string elementId);
}
