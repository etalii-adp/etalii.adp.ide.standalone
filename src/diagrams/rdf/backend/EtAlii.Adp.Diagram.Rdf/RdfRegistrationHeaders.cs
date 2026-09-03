namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// Reads a reading's own <c>key: value</c> headers from a registration file - the registration
/// header facility the family's specifications name as shared-machinery item 10, written once
/// here for every reading. It scans; the reading interprets.
/// </summary>
/// <remarks>
/// The facility's rules, enforced by construction rather than by checking: a reading's header
/// sits after core's <c>body:</c>/<c>view:</c> and before the <c>layout:</c> block (core's
/// pairing scan stops at the first line it does not recognize, so a header above <c>body:</c>
/// would silently sever the pairing), its name is a single lowercase word claimed in the anchor
/// specification before use, and each reading parses its own - core never learns them (the
/// <c>view:</c>/<c>resource:</c> precedent).
/// </remarks>
public static class RdfRegistrationHeaders
{
    /// <summary>How many lines after the MIME line are scanned before giving up - core's own limit.</summary>
    private const int HeaderScanLimit = 8;

    /// <summary>
    /// The value of the <paramref name="key"/> header (without its colon), or null where the
    /// registration has none, cannot be read, or the header region ended first.
    /// </summary>
    public static string? Read(string? registrationPath, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        if (registrationPath is not { Length: > 0 })
        {
            return null;
        }

        var header = key + ":";
        try
        {
            using var reader = new StreamReader(registrationPath);
            reader.ReadLine(); // the MIME line
            for (var scanned = 0; scanned < HeaderScanLimit; scanned++)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    return null;
                }

                var trimmed = line.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }

                // The layout: block starts the position region; headers do not follow it.
                if (trimmed.StartsWith("layout:", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (trimmed.StartsWith(header, StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed[header.Length..].Trim();
                    return value.Length == 0 ? null : value;
                }
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
