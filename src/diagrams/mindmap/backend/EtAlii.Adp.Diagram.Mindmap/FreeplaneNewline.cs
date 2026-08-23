using System.Xml.Linq;

namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// The newline to synthesize when editing a map's structure: the one the file itself uses
/// between its node elements. A Windows Freeplane save is CRLF there (while keeping LF inside
/// its embedded richcontent HTML - see Fixtures/readme.md), a hand-written or ADP-created map
/// is LF, and an edit must match its file or every undo and every insertion leaves a stray
/// line-ending diff behind.
/// </summary>
internal static class FreeplaneNewline
{
    /// <summary>
    /// The structural newline of the document <paramref name="element"/> belongs to, read from
    /// the first whitespace under the map root - the text right after <c>&lt;map ...&gt;</c>,
    /// which is always structural. <c>\n</c> when there is none (a document being built).
    /// </summary>
    internal static string Of(XElement element)
    {
        var root = element.Document?.Root;
        var first = root?.Nodes().OfType<XText>().FirstOrDefault(text => text.Value.Contains('\n'));
        return first is not null && first.Value.Contains("\r\n") ? "\r\n" : "\n";
    }
}
