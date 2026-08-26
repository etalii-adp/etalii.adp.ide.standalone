namespace EtAlii.Adp.Diagram;

/// <summary>
/// Judges the documents of one diagram type - the one seam a diagram-type module implements
/// to have rules. Core resolves it by <see cref="Origin"/> through
/// <see cref="DiagramValidators"/> and never names the type.
/// </summary>
/// <remarks>
/// Registered like <see cref="IDiagramDocumentFactory"/>. Unlike a document factory, whose
/// absence <see cref="DiagramDocumentFactories.Verify"/> reports at startup, a type with no
/// rules simply registers nothing: a diagram type must be able to create a document, but it
/// need not have opinions about one.
/// </remarks>
public interface IDiagramValidator
{
    /// <summary>The diagram type whose documents this judges.</summary>
    DiagramOrigin Origin { get; }

    /// <summary>
    /// The problems in the diagram <paramref name="request"/> describes, or none.
    /// </summary>
    /// <remarks>
    /// A validator judging a document reads <see cref="DiagramValidationRequest.Document"/>; one
    /// judging a folder reads <see cref="DiagramValidationRequest.SubjectFolder"/>. Either way
    /// the paths arrive already resolved and containment-checked, so a rule never resolves a
    /// location itself.
    /// </remarks>
    ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        DiagramValidationRequest request, CancellationToken cancellationToken);
}
