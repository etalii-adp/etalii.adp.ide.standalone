using System.Text;
using System.Text.RegularExpressions;

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

    /// <summary>Why a document of this envelope cannot be changed here, or empty when its version is one this application reads.</summary>
    public string ReadOnlyReason => KnownVersions.Contains(Version, StringComparer.Ordinal)
        ? ""
        : $"This document is written in DED {Version}, and this application reads DED {KnownVersions[^1]} and earlier. It is left exactly as it is and cannot be changed here.";

    /// <summary>DED 0.2 section 2's expression for the Markdown envelope.</summary>
    [GeneratedRegex(@"^<!--[ \t]+ded:[ \t]+(?<ded>[^ \t]+)[ \t]+designer:[ \t]+(?<designer>[^ \t]+)[ \t]+-->[ \t]*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex Line();
}
