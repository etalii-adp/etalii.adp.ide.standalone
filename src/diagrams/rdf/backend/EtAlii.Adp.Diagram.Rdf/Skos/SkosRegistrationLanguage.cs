using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The scheme reading's <c>language:</c> header - the registration header facility's claimed key
/// for <c>w3c/skos</c>, with the reading's own band guard on top.
/// </summary>
/// <remarks>
/// The facility places a reading's header after <c>body:</c>/<c>view:</c> and before
/// <c>layout:</c>, because core's pairing scan breaks at the first line it does not recognize -
/// so a <c>body:</c> or <c>view:</c> line BELOW a <c>language:</c> line is never reached, and
/// the registration silently loses its document. That is the precise hazard, and it is the
/// precise check: a <c>language:</c> header with a core header below it is <b>ignored and
/// reported</b>, never honoured and never guessed at; one with no core header below it sits in
/// the band by construction, wherever the band began (skos-diagram Requirements 3.2, 7). The
/// scan reads through <see cref="SharedDocumentReader"/> so it cannot contend with a rename's
/// in-place rewrite of the registration.
/// </remarks>
public static class SkosRegistrationLanguage
{
    /// <summary>The header key item 10 claims for this reading.</summary>
    public const string Key = "language";

    /// <summary>Core's own header-region limit, matched so the two scans agree on the region.</summary>
    private const int HeaderScanLimit = 8;

    /// <summary>
    /// The display language the registration declares, and whether the <c>language:</c> line
    /// sits above a core header it would sever. A misplaced header yields a null language - the
    /// default order applies - with <c>MisplacedLine</c> carrying its 1-based line for the
    /// validator (0 otherwise).
    /// </summary>
    public static (string? Language, int MisplacedLine) Read(string? registrationPath)
    {
        if (registrationPath is not { Length: > 0 })
        {
            return (null, 0);
        }

        string? language = null;
        var languageLine = 0;
        var coreHeaderBelowLanguage = false;

        try
        {
            using var reader = SharedDocumentReader.OpenText(registrationPath);
            reader.ReadLine(); // the MIME line
            for (var scanned = 0; scanned < HeaderScanLimit; scanned++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                if (trimmed.StartsWith("layout:", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (trimmed.StartsWith("body:", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("view:", StringComparison.OrdinalIgnoreCase))
                {
                    coreHeaderBelowLanguage |= languageLine > 0;
                    continue;
                }

                if (languageLine == 0 && trimmed.StartsWith(Key + ":", StringComparison.OrdinalIgnoreCase))
                {
                    languageLine = scanned + 2; // 1-based: the MIME line is line 1.
                    var value = trimmed[(Key.Length + 1)..].Trim();
                    language = value.Length == 0 ? null : value.ToLowerInvariant();
                }

                // Anything else is another reading's header or prose - not ours to interpret.
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, 0);
        }

        return coreHeaderBelowLanguage
            ? (null, languageLine)
            : (language, 0);
    }
}
