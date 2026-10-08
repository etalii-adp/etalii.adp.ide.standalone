using System.Text;
using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Text;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>
/// Evaluates a marker (FBL §12.2) on a body's bytes without reading it through a binding: a root key
/// of a yaml or json body, a prefix of the first line after a byte-order mark, or an expression one
/// of the first lines matches.
/// </summary>
public static class MarkerEvaluator
{
    /// <summary>The number of lines a pattern marker looks at when it names none (FBL §12.2).</summary>
    private const int DefaultLines = 20;

    public static bool Matches(Marker marker, byte[] bytes, TimeSpan? regexTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(bytes);
        var text = new BodyText(bytes);
        if (!text.IsValidUtf8) return false;
        if (marker.RootKey is { } key) return RootKey(text, key, marker.RootValue is { } value ? BindingReader.ScalarText(value) : null);
        if (marker.FirstLine is { } prefix)
        {
            return text.Lines.Count > 0 && text.Text(Math.Max(text.Lines[0].Start, text.BomLength), text.Lines[0].ContentEnd).StartsWith(prefix, StringComparison.Ordinal);
        }
        if (marker.Pattern is { } pattern)
        {
            var regex = new BoundedRegex(pattern, false, regexTimeout ?? TimeSpan.FromMilliseconds(250));
            var lines = marker.Lines > 0 ? marker.Lines : DefaultLines;
            foreach (var line in text.Lines.Take(lines))
            {
                if (regex.IsMatch(text.Text(Math.Max(line.Start, text.BomLength), line.ContentEnd))) return true;
            }
        }
        return false;
    }

    /// <summary>A root key of a yaml or json body (json is read as the yaml it also is), with its scalar value when one is asked.</summary>
    private static bool RootKey(BodyText text, string key, string? value)
    {
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(Encoding.UTF8.GetString(text.Bytes, text.BomLength, text.Length - text.BomLength)));
            if (stream.Documents.Count == 0 || stream.Documents[0].RootNode is not YamlMappingNode root) return false;
            foreach ((YamlNode name, YamlNode node) in root.Children)
            {
                if (name is not YamlScalarNode { Value: { } found } || found != key) continue;
                return value is null || (node is YamlScalarNode scalar && scalar.Value == value);
            }
            return false;
        }
        catch (YamlDotNet.Core.YamlException)
        {
            return false;
        }
    }
}
