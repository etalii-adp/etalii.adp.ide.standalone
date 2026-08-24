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
    /// The problems in <paramref name="document"/>, or none. <paramref name="baseName"/> is
    /// the diagram's file base name, for messages that want to name it.
    /// </summary>
    ValueTask<IReadOnlyList<DiagramProblem>> ValidateAsync(
        string document, string baseName, CancellationToken cancellationToken);
}
