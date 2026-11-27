using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Text;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>
/// Evaluates a marker (FBL §12.2) on a body's bytes without reading it through a binding: a root key
/// of a yaml or json body, a prefix of the first line after a byte-order mark, or an expression one
/// of the first lines matches.
/// </summary>
internal static class MarkerEvaluator
{
    /// <summary>The number of lines a pattern marker looks at when it names none (FBL §12.2).</summary>
    private const int DefaultLines = 20;

    public static bool Matches(Marker marker, byte[] bytes, TimeSpan? regexTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(bytes);
        // A line marker looks at its lines and nothing after them, so a byte that is no UTF-8 further on, which the binding
        // reports when it reads the file, does not hide the mark.
        var lines = marker.FirstLine is not null ? 1 : marker.Pattern is not null ? (marker.Lines > 0 ? marker.Lines : DefaultLines) : 0;
        var text = new BodyText(lines > 0 ? FirstLines(bytes, lines) : bytes);
        if (!text.IsValidUtf8) return false;
        if (marker.RootKey is { } key) return RootKey(text, key, marker.RootValue is { } value ? BindingReader.ScalarText(value) : null);
        if (marker.FirstLine is { } prefix)
        {
            return text.Lines.Count > 0 && text.Text(Math.Max(text.Lines[0].Start, text.BomLength), text.Lines[0].ContentEnd).StartsWith(prefix, StringComparison.Ordinal);
        }
        if (marker.Pattern is { } pattern)
        {
            var regex = new BoundedRegex(pattern, false, regexTimeout ?? FblOptions.DefaultRegexTimeout);
            foreach (var line in text.Lines.Take(lines))
            {
                if (regex.IsMatch(text.Text(Math.Max(line.Start, text.BomLength), line.ContentEnd))) return true;
            }
        }
        return false;
    }

    /// <summary>The bytes of the first <paramref name="count"/> lines, their last line end included.</summary>
    private static byte[] FirstLines(byte[] bytes, int count)
    {
        var end = -1;
        for (var line = 0; line < count; line++)
        {
            end = Array.IndexOf(bytes, (byte)'\n', end + 1);
            if (end < 0) return bytes;
        }
        return bytes[..(end + 1)];
    }

    /// <summary>
    /// A root key of a yaml or json body (json is read as the yaml it also is), with its scalar value when one is asked. A body
    /// that does not parse whole, such as the head of a large file cut off in the middle, is read up to its first error, so a
    /// key whose value was read whole before it is still found (FBL §12.2: a marker is evaluated on a body's first bytes).
    /// </summary>
    private static bool RootKey(BodyText text, string key, string? value)
    {
        var body = Encoding.UTF8.GetString(text.Bytes, text.BomLength, text.Length - text.BomLength);
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(body));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) return false;
            foreach ((YamlNode name, YamlNode node) in root.Children)
            {
                if (name is not YamlScalarNode { Value: { } found } || found != key) continue;
                return value is null || (node is YamlScalarNode scalar && scalar.Value == value);
            }
            return false;
        }
        catch (YamlException)
        {
            return RootKeyBeforeError(body, key, value);
        }
    }

    /// <summary>
    /// The scalar value of a root key of a yaml or json body, read up to the body's first parse error as a marker's root key is;
    /// null when the key is not found before it or its value is no scalar.
    /// </summary>
    public static string? RootScalar(byte[] bytes, string key)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(key);
        var text = new BodyText(bytes);
        if (!text.IsValidUtf8) return null;
        var body = Encoding.UTF8.GetString(text.Bytes, text.BomLength, text.Length - text.BomLength);
        return RootEntry(body, key, out var scalar) ? scalar : null;
    }

    /// <summary>
    /// Walks the root mapping's keys event by event and stops at the key, at the mapping's end or at the first parse error. The
    /// key counts only once its whole value is read, so a value the error cuts short, such as an unclosed sequence, is no match.
    /// </summary>
    private static bool RootKeyBeforeError(string body, string key, string? value) =>
        RootEntry(body, key, out var scalar) && (value is null || scalar == value);

    /// <summary>Whether the root mapping holds <paramref name="key"/> with its whole value before any parse error, and that value when it is a scalar.</summary>
    private static bool RootEntry(string body, string key, out string? scalar)
    {
        scalar = null;
        try
        {
            var parser = new Parser(new StringReader(body));
            parser.Consume<StreamStart>();
            if (!parser.Accept<DocumentStart>(out _)) return false;
            parser.Consume<DocumentStart>();
            if (!parser.Accept<MappingStart>(out _)) return false;
            parser.Consume<MappingStart>();
            while (!parser.Accept<MappingEnd>(out _))
            {
                var named = parser.Accept<Scalar>(out var name) ? name.Value : null;
                if (named is null) parser.SkipThisAndNestedEvents();
                else parser.Consume<Scalar>();
                if (named != key)
                {
                    parser.SkipThisAndNestedEvents();
                    continue;
                }
                if (parser.Accept<Scalar>(out var value))
                {
                    scalar = value.Value;
                    return true;
                }
                parser.SkipThisAndNestedEvents();
                return true;
            }
            return false;
        }
        catch (YamlException)
        {
            return false;
        }
    }
}
