using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// An <see cref="IDiagramDocumentFactory"/> whose new document is one fixed text, so a module
/// registers that text rather than writing a class to return it (code-structure FR-030).
/// </summary>
/// <remarks>
/// <b>The text is the same whatever the file is called.</b> A type whose new document depends on
/// the base name - a mind map's root node is named after it - keeps its own factory.
/// </remarks>
public sealed class EmptyDocumentFactory : IDiagramDocumentFactory
{
    private readonly string _text;

    public EmptyDocumentFactory(DiagramOrigin origin, string text)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(text);
        Origin = origin;
        _text = text;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin { get; }

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return _text;
    }
}
