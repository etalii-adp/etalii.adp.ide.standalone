using System.Text;
using EtAlii.Adp.Specification.Fbl.Links;

namespace EtAlii.Adp.Links.Tests;

/// <summary>
/// A tool type of the tests' own, whose files end in <c>.lnk.txt</c> and hold one line per fact: <c>id: X</c> for the document
/// id, <c>element: ID Name</c> for a linkable element and <c>link: HOLDER TEXT</c> for a link an element holds. A file whose
/// first line is <c>throw</c> cannot be read.
/// </summary>
internal sealed class LinkIndexTestIndexer : ILinkIndexer
{
    private const string Extension = ".lnk.txt";

    public int Reads { get; private set; }

    public string? Origin => "test/links";

    public bool Claims(string path) => path.EndsWith(Extension, StringComparison.Ordinal);

    public IndexedDocument Index(string path, byte[] bytes)
    {
        Reads++;
        var lines = Encoding.UTF8.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines is ["throw", ..]) throw new InvalidDataException("The test asked for a file that cannot be read.");
        string? documentId = null;
        var elements = new List<IndexedElement>();
        var links = new List<HeldLink>();
        foreach (var line in lines)
        {
            (string key, string value) = (line[..line.IndexOf(':', StringComparison.Ordinal)], line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim());
            var parts = value.Split(' ', 2);
            switch (key)
            {
                case "id":
                    documentId = value;
                    break;
                case "element":
                    elements.Add(new IndexedElement(parts[0], [new KeyValuePair<string, string>("name", parts[1])]));
                    break;
                case "link":
                    links.Add(new HeldLink(parts[0], parts[1]));
                    break;
            }
        }
        return new IndexedDocument(path, documentId, "name", elements, links);
    }

    public byte[]? Renewed(string path, byte[] bytes)
    {
        var lines = Encoding.UTF8.GetString(bytes).Split('\n');
        return lines.Any(line => line.StartsWith("fixed", StringComparison.Ordinal))
            ? null
            : Encoding.UTF8.GetBytes(string.Join('\n', lines.Select(line => line.StartsWith("id:", StringComparison.Ordinal) ? "id: renewed" : line)));
    }
}
