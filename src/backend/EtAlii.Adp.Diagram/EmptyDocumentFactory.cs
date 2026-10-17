using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// An <see cref="IDiagramDocumentFactory"/> made of a registration: a module registers the text
/// of a new document, or how to write it from the file's base name, rather than writing a class
/// to return it (code-structure FR-029, FR-030).
/// </summary>
public sealed class EmptyDocumentFactory : IDiagramDocumentFactory
{
    private readonly Func<string, string> _textFromBaseName;

    /// <summary>A factory whose new document is <paramref name="text"/>, whatever the file is called.</summary>
    public EmptyDocumentFactory(ToolOrigin origin, string text)
        : this(origin, Fixed(text))
    {
    }

    /// <summary>A factory whose new document <paramref name="textFromBaseName"/> writes from the file's base name.</summary>
    public EmptyDocumentFactory(ToolOrigin origin, Func<string, string> textFromBaseName)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(textFromBaseName);
        Origin = origin;
        _textFromBaseName = textFromBaseName;
    }

    /// <inheritdoc />
    public ToolOrigin Origin { get; }

    /// <inheritdoc />
    public string CreateEmptyDocument(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        return _textFromBaseName(baseName);
    }

    private static Func<string, string> Fixed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _ => text;
    }
}
