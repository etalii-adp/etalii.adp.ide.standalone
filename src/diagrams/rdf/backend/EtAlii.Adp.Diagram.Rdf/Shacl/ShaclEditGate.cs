namespace EtAlii.Adp.Diagram.Rdf.Shacl;

/// <summary>
/// Whether a selected element may be edited through this reading, and the sentence to say when it
/// may not - decided from the selection and the parsed model alone.
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>provider</em> half of the double defence Requirement 3.3 asks for. The writer
/// refuses a blank-rooted mutation when it is called directly, bypassing every provider; this
/// decides the same thing without consulting the writer, so the menu can mark a gesture
/// unavailable before anything is executed. Neither layer delegates its refusal to the other,
/// because a defence implemented in one and assumed in the other is how such refusals erode - and
/// both answer with the same <see cref="ShaclRefusals"/> constant, so a user who meets both reads
/// one refusal rather than two that drifted apart.
/// </para>
/// <para>
/// It also scopes this reading's verbs: a shapes gesture is offered only where the selected term
/// is a shape as the file states it, so opening an ordinary data graph never grows a menu of
/// constraint verbs.
/// </para>
/// </remarks>
public static class ShaclEditGate
{
    /// <summary>What the gate decided: whether to offer the verbs, and why not when it did not.</summary>
    /// <param name="Applies">Whether this reading has anything to say about the selection at all.</param>
    /// <param name="Available">Whether its gestures may be performed.</param>
    /// <param name="Reason">The sentence to show when they may not; empty when they may.</param>
    /// <param name="ShapeIri">The IRI-named shape the gestures act on; empty when there is none.</param>
    public readonly record struct Decision(bool Applies, bool Available, string Reason, string ShapeIri);

    /// <summary>Nothing here for this reading - another type's element, or not a shape.</summary>
    public static readonly Decision NotApplicable = new(false, false, "", "");

    /// <summary>
    /// Decides for one selected element id against one parsed model.
    /// </summary>
    public static Decision For(RdfModel model, string? elementId, bool truncated)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (string.IsNullOrEmpty(elementId))
        {
            return NotApplicable;
        }

        var shapes = ShaclShapeDiscovery.Discover(model);

        if (elementId.StartsWith("blank:", StringComparison.Ordinal))
        {
            // An anonymous shape draws and describes, and refuses every mutation: the boundary.
            var isShape = shapes.Any(shape => ShaclProjection.IdOf(shape.Key) == elementId);
            return isShape
                ? new Decision(Applies: true, Available: false, ShaclRefusals.BlankRooted, ShapeIri: "")
                : NotApplicable;
        }

        if (!elementId.StartsWith("res:", StringComparison.Ordinal))
        {
            return NotApplicable;
        }

        var iri = elementId["res:".Length..];
        if (!shapes.Any(shape => shape.Term is IriTerm term && term.Iri == iri))
        {
            // A term this file does not state to be a shape - the data-graph reading's business.
            return NotApplicable;
        }

        // A truncated view withholds edits for the same reason every reading here does: an edit
        // through a partial view could touch what the view does not show.
        return truncated
            ? new Decision(Applies: true, Available: false, RdfSelection.TruncatedRefusal, iri)
            : new Decision(Applies: true, Available: true, "", iri);
    }
}
