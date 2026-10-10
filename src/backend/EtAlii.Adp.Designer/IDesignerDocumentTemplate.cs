namespace EtAlii.Adp.Designer;

/// <summary>
/// What a new document of one designer type starts as, per format. A designer module that
/// declares <see cref="DesignerDefinition.Formats"/> registers one of these, as a diagram module
/// whose type keeps a body file registers a document factory. The module takes the content from
/// its binding's template for the format, so a new document is what the designer's
/// specification says an empty one is (knowledge-designer Requirement 10.4).
/// </summary>
public interface IDesignerDocumentTemplate
{
    /// <summary>The designer type this serves; matches <see cref="DesignerDefinition.Origin"/>.</summary>
    string Origin { get; }

    /// <summary>
    /// The complete content of a new document named <paramref name="fileName"/> in
    /// <paramref name="format"/>, or null when this designer has no document of that format.
    /// </summary>
    /// <param name="format">One of the definition's formats.</param>
    /// <param name="fileName">The new body file's name, extension included.</param>
    string? Create(DesignerFormat format, string fileName);
}
