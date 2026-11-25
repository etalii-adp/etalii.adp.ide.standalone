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
    /// <summary>The characters that end an id, an element id or a name property, and so are escaped in one (FBL 17.1).</summary>
    private const string EndsAnId = "$@#.=";

    /// <summary>The characters that end a document name.</summary>
    private const string EndsAFileName = "#";

    /// <summary>
    /// The link <paramref name="text"/> holds, or null when it does not parse (<c>link.malformed</c>, FBL 17.6): the text is
    /// split at its separators first, the document id ending at the first <c>@</c>, the document name at the first <c>#</c>,
    /// the element id at the first <c>.</c> or <c>=</c> and the name property at the first <c>=</c>, and each part is unescaped
    /// after, the hexadecimal digits read in either case. A part that holds a character its grammar does not allow is no link.
    /// </summary>
    public static FblLink? Parse(string? text)
    {
        if (text is null || text.Length < 2 || text[0] != '$') return null;
        var at = text.IndexOf('@', 1);
        if (at < 0) return null;
        var hash = text.IndexOf('#', at + 1);
        var documentId = Unescape(text[1..at], EndsAnId, false);
        var documentName = Unescape(hash < 0 ? text[(at + 1)..] : text[(at + 1)..hash], EndsAFileName, false);
        if (documentId is null || documentName is null) return null;
        if (hash < 0) return new FblLink(documentId, documentName, null, null, null);

        var rest = text[(hash + 1)..];
        var equals = rest.IndexOf('=');
        var head = equals < 0 ? rest : rest[..equals];
        var dot = head.IndexOf('.');
        var elementId = Unescape(dot < 0 ? head : head[..dot], EndsAnId, false);
        var nameProperty = dot < 0 ? null : Unescape(head[(dot + 1)..], EndsAnId, false);
        var name = equals < 0 ? null : Unescape(rest[(equals + 1)..], "", true);
        if (elementId is null || (dot >= 0 && nameProperty is null) || (equals >= 0 && name is null)) return null;
        return new FblLink(documentId, documentName, elementId, nameProperty, name);
    }

    /// <summary>A link to the document <paramref name="documentName"/> of the id <paramref name="documentId"/> as a whole.</summary>
    public static FblLink ToDocument(string documentId, string documentName) => new(documentId, documentName, null, null, null);

    /// <summary>
    /// The link as it is written (FBL 17.1): each part escaped only where a character would end it, and <c>%</c> in every
    /// part, as the two upper-case hexadecimal digits of each of its UTF-8 bytes.
    /// </summary>
    public string Text
    {
        get
        {
            var text = new StringBuilder("$").Append(Escape(DocumentId, EndsAnId)).Append('@').Append(Escape(DocumentName, EndsAFileName));
            if (ElementId is null) return text.ToString();
            text.Append('#').Append(Escape(ElementId, EndsAnId));
            if (NameProperty is not null) text.Append('.').Append(Escape(NameProperty, EndsAnId));
            if (Name is not null) text.Append('=').Append(Escape(Name, ""));
            return text.ToString();
        }
    }

    /// <summary>
    /// <paramref name="part"/> with each <c>%</c> and two hexadecimal digits read as a UTF-8 byte; null when it is empty and may
    /// not be, a <c>%</c> is not followed by two, it holds a control character or one of <paramref name="reserved"/> as it is, or
    /// the bytes are no UTF-8.
    /// </summary>
    private static string? Unescape(string part, string reserved, bool mayBeEmpty)
    {
        if (part.Length == 0) return mayBeEmpty ? part : null;
        var bytes = new List<byte>(part.Length);
        var buffer = new byte[4];
        for (var index = 0; index < part.Length;)
        {
            if (part[index] == '%')
            {
                if (index + 3 > part.Length || !byte.TryParse(part.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value)) return null;
                bytes.Add(value);
                index += 3;
                continue;
            }
            if (Rune.DecodeFromUtf16(part.AsSpan(index), out var rune, out var used) != System.Buffers.OperationStatus.Done) return null;
            if (Rune.IsControl(rune) || (rune.IsAscii && reserved.Contains((char)rune.Value, StringComparison.Ordinal))) return null;
            var written = rune.EncodeToUtf8(buffer);
            bytes.AddRange(buffer.AsSpan(0, written));
            index += used;
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

    /// <summary><paramref name="text"/> with <c>%</c> and each of <paramref name="reserved"/> escaped.</summary>
    private static string Escape(string text, string reserved)
    {
        var escaped = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c != '%' && !reserved.Contains(c, StringComparison.Ordinal))
            {
                escaped.Append(c);
                continue;
            }
            escaped.Append('%').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
        }
        return escaped.ToString();
    }
}
