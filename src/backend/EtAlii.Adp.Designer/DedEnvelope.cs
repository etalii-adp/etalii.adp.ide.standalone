using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Designer;

/// <summary>
/// The envelope of a DED definition in its Markdown form, line 1 of the file (DED 0.2 section 2): the version of DED
/// and the designer type's origin. A host opens a definition of a later version read-only, with the reason, and never
/// writes an envelope after the template (DED sections 4 and 5).
/// </summary>
/// <remarks>
/// The Knowledge designer's documents state their envelope in YAML, JSON and XML, and its bindings require the version
/// they read as their header, so a later version is unreadable there before any envelope is looked at.
/// </remarks>
public sealed partial record DedEnvelope(string Version, string Designer)
{
    /// <summary>
    /// The extensions DED states an envelope in (DED sections 2, 4 and 5): the shared extensions of the YAML, JSON, XML and
    /// Markdown forms, and <c>.ded</c> for the JSON form. DED's own, not any one designer type's.
    /// </summary>
    public static IReadOnlyList<string> Extensions { get; } = [".yaml", ".yml", ".json", ".ded", ".xml", ".md"];

    /// <summary>The versions of DED this application reads; the last is the one it writes.</summary>
    private static IReadOnlyList<string> KnownVersions { get; } = ["0.1", "0.2"];

    /// <summary>The envelope on line 1 of <paramref name="bytes"/>, after a byte-order mark only, or null when line 1 is none.</summary>
    public static DedEnvelope? Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var start = bytes.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        var end = Array.IndexOf(bytes, (byte)'\n', start);
        var line = Encoding.UTF8.GetString(bytes, start, (end < 0 ? bytes.Length : end) - start).TrimEnd('\r');
        var match = Line().Match(line);
        return match.Success ? new DedEnvelope(match.Groups["ded"].Value, match.Groups["designer"].Value) : null;
    }

    /// <summary>
    /// The envelope a document's head carries in whichever form its format states it (DED section 2): line 1 of a Markdown
    /// document, the root keys <c>ded</c> and <c>designer</c> of a YAML or JSON one, or the <c>ded</c> root element's
    /// <c>designer</c> attribute of an XML one. Null for a head that carries none, such as an ordinary file with a
    /// <c>designer</c> key and no <c>ded</c> beside it. It names no designer type, so a host can say which type a file
    /// names when none of the installed ones takes it (spec 093 FR-006).
    /// </summary>
    public static DedEnvelope? Find(byte[] head)
    {
        ArgumentNullException.ThrowIfNull(head);
        if (Read(head) is { } markdown)
        {
            return markdown;
        }

        if (BodyClaim.RootScalar(head, "designer") is { Length: > 0 } designer && BodyClaim.RootScalar(head, "ded") is { Length: > 0 } version)
        {
            return new DedEnvelope(version, designer);
        }

        var start = head.AsSpan().StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? 3 : 0;
        var lines = Encoding.UTF8.GetString(head, start, head.Length - start).Split('\n').Take(XmlLines);
        return lines.Select(line => XmlRoot().Match(line)).FirstOrDefault(match => match.Success) is { } root
            ? new DedEnvelope(root.Groups["ded"].Success ? root.Groups["ded"].Value : "", root.Groups["designer"].Value)
            : null;
    }

    /// <summary>Why a document of this envelope cannot be changed here, or empty when its version is one this application reads.</summary>
    public string ReadOnlyReason => KnownVersions.Contains(Version, StringComparer.Ordinal)
        ? ""
        : $"This document is written in DED {Version}, and this application reads DED {KnownVersions[^1]} and earlier. It is left exactly as it is and cannot be changed here.";

    /// <summary>The lines of an XML head its <c>ded</c> root element is looked for in, as FBL section 12.2 gives a pattern marker.</summary>
    private const int XmlLines = 20;

    /// <summary>The <c>ded</c> root element of an XML document, with its <c>designer</c> attribute and, where it has one, its version.</summary>
    [GeneratedRegex(@"^<ded\s(?=[^>]*\bdesigner=""(?<designer>[^""]+)"")(?:[^>]*\b(?:version|ded)=""(?<ded>[^""]+)"")?", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex XmlRoot();

    /// <summary>DED 0.2 section 2's expression for the Markdown envelope.</summary>
    [GeneratedRegex(@"^<!--[ \t]+ded:[ \t]+(?<ded>[^ \t]+)[ \t]+designer:[ \t]+(?<designer>[^ \t]+)[ \t]+-->[ \t]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Line();
}
