using System.Globalization;
using System.Text;

namespace EtAlii.Adp.Specification.Fbl.Links;

/// <summary>
/// A link to a document or an element of one (FBL 17.1): <c>$document-id@document-name[#element-id[.name-prop][=name]]</c>,
/// each part unescaped. <see cref="ElementId"/> is null for a link that names a document only; <see cref="NameProperty"/>
/// and <see cref="Name"/> are null where the link holds none.
/// </summary>
public sealed record FblLink(string DocumentId, string DocumentName, string? ElementId, string? NameProperty, string? Name)
{
    /// <summary>
    /// The link <paramref name="text"/> holds, or null when it does not parse (<c>link.malformed</c>, FBL 17.6): the text is
    /// split at its separators first, the document id ending at the first <c>@</c>, the document name at the first <c>#</c>,
    /// the element id at the first <c>.</c> or <c>=</c> and the name property at the first <c>=</c>, and each part is unescaped
    /// after, the hexadecimal digits read in either case.
    /// </summary>
    public static FblLink? Parse(string? text)
    {
        if (text is null || text.Length < 2 || text[0] != '$') return null;
        var at = text.IndexOf('@', 1);
        if (at < 0) return null;
        var hash = text.IndexOf('#', at + 1);
        var documentId = Unescape(text[1..at]);
        var documentName = Unescape(hash < 0 ? text[(at + 1)..] : text[(at + 1)..hash]);
        if (documentId is not { Length: > 0 } || documentName is not { Length: > 0 }) return null;
        if (hash < 0) return new FblLink(documentId, documentName, null, null, null);

        var rest = text[(hash + 1)..];
        var equals = rest.IndexOf('=');
        var head = equals < 0 ? rest : rest[..equals];
        var dot = head.IndexOf('.');
        var elementId = Unescape(dot < 0 ? head : head[..dot]);
        var nameProperty = dot < 0 ? null : Unescape(head[(dot + 1)..]);
        var name = equals < 0 ? null : Unescape(rest[(equals + 1)..]);
        if (elementId is not { Length: > 0 } || (dot >= 0 && nameProperty is not { Length: > 0 }) || (equals >= 0 && name is null)) return null;
        return new FblLink(documentId, documentName, elementId, nameProperty, name);
    }

    /// <summary>
    /// <paramref name="part"/> with each <c>%</c> and two hexadecimal digits read as a UTF-8 byte; null when a <c>%</c> is
    /// not followed by two, a control character is written as it is, or the bytes are no UTF-8.
    /// </summary>
    private static string? Unescape(string part)
    {
        if (part.Any(char.IsControl)) return null;
        if (!part.Contains('%', StringComparison.Ordinal)) return part;
        var bytes = new List<byte>(part.Length);
        for (var index = 0; index < part.Length; index++)
        {
            if (part[index] != '%')
            {
                bytes.AddRange(Encoding.UTF8.GetBytes(part[index].ToString()));
                continue;
            }
            if (index + 2 >= part.Length || !byte.TryParse(part.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return null;
            bytes.Add(value);
            index += 2;
        }
        try
        {
            return new UTF8Encoding(false, true).GetString([.. bytes]);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
