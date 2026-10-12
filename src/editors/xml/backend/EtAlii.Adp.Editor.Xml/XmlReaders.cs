using System.Xml;
using System.Xml.Schema;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// The one place this module creates an <see cref="XmlReader"/> or an <see cref="XmlSchemaSet"/>,
/// so the resolver and the limits cannot be forgotten at one call site (xml-editor Security).
/// Every reader is given the project's <see cref="ProjectXmlResolver"/>, parses a DTD so its
/// entities and its validation work, bounds what entities may expand to, and keeps line
/// information for the findings. A test over the module's source holds that nothing else
/// creates one.
/// </summary>
internal static class XmlReaders
{
    /// <summary>
    /// The most characters entities may expand to in one document: a document built to expand
    /// without end is refused with the platform's sentence rather than read (xml-editor Q4).
    /// </summary>
    public const long MaximumCharactersFromEntities = 1_000_000;

    /// <summary>The most characters a document may hold once read: ten times the most a request may carry.</summary>
    public const long MaximumCharactersInDocument = 10_000_000;

    /// <summary>A reader over <paramref name="content"/>, the file at <paramref name="fullPath"/> as typed.</summary>
    public static XmlReader Document(string content, string fullPath, ProjectXmlResolver resolver, Action<XmlReaderSettings>? configure = null)
    {
        var settings = Settings(resolver);
        configure?.Invoke(settings);
        return XmlReader.Create(new StringReader(content), settings, ProjectXmlResolver.BaseUriOf(fullPath));
    }

    /// <summary>A schema set that reads every include, import and redefine through <paramref name="resolver"/>.</summary>
    public static XmlSchemaSet SchemaSet(ProjectXmlResolver resolver) => new() { XmlResolver = resolver };

    /// <summary>Reads one schema document, the root of a set, from <paramref name="content"/>.</summary>
    public static XmlReader Schema(string content, string fullPath, ProjectXmlResolver resolver) =>
        XmlReader.Create(new StringReader(content), Settings(resolver), ProjectXmlResolver.BaseUriOf(fullPath));

    private static XmlReaderSettings Settings(ProjectXmlResolver resolver) => new()
    {
        XmlResolver = resolver,
        DtdProcessing = DtdProcessing.Parse,
        MaxCharactersFromEntities = MaximumCharactersFromEntities,
        MaxCharactersInDocument = MaximumCharactersInDocument,
        IgnoreWhitespace = false,
    };
}
