namespace EtAlii.Adp.Designer;

/// <summary>
/// One file format a designer's document can be created in: the name the author chooses it by
/// and the extension the new file takes. The format is chosen when the document is added and
/// is not changed afterwards (knowledge-designer Requirements 2.4 and 2.5).
/// </summary>
/// <param name="Title">The name shown where the format is chosen, e.g. <c>"YAML"</c>.</param>
/// <param name="Extension">
/// The extension of a new document in this format, dot included; normalised to lower case on
/// construction so lookup never depends on how a module happened to type it.
/// </param>
public sealed record DesignerFormat(string Title, string Extension)
{
    /// <summary>The extension, dot included, lower-cased by construction.</summary>
    public string Extension { get; } = Extension.ToLowerInvariant();
}
