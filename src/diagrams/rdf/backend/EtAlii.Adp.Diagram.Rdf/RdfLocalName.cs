using System.Text;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// A file's base name as a local name, for the document each reading of the family starts a new
/// diagram as: written once for the four readings, which each had a copy (code-structure FR-035).
/// </summary>
internal static class RdfLocalName
{
    /// <summary>
    /// <paramref name="baseName"/> with anything a prefixed name cannot carry folded to
    /// underscores, so "My Graph.data" becomes <c>My_Graph_data</c>; "untitled" when nothing is left.
    /// </summary>
    public static string Of(string baseName)
    {
        var builder = new StringBuilder(baseName.Length);
        foreach (var character in baseName)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_');
        }

        var local = builder.ToString().Trim('_');
        return local.Length > 0 ? local : "untitled";
    }
}
