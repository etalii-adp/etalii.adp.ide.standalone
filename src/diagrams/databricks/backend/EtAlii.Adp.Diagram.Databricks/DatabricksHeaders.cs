namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Reads this family's own headers from a registration file - the <c>resource:</c> key that
/// picks one declaration out of a multi-resource file. Read here rather than in core: core
/// hands the module its own file and interprets nothing in it (the C4 view-key precedent).
/// </summary>
internal static class DatabricksHeaders
{
    /// <summary>
    /// The <c>resource:</c> header's value, or null when the registration has none - in which
    /// case the file's first matching declaration is the diagram.
    /// </summary>
    public static string? ReadResourceKey(string? registrationPath)
    {
        if (registrationPath is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            // Shared, per the repository's shared-read discipline: a rename rewrites .adp
            // headers in place, and a default-share reader would refuse - and be refused by -
            // that write on Windows. (The same one-line fix RdfRegistrationHeaders took under
            // the family coordination ruling of 2026-09-03; these two helpers postdated the
            // shared-read audit and are deliberately identical.)
            using var reader = EtAlii.Adp.Backend.Hierarchy.SharedDocumentReader.OpenText(registrationPath);
            reader.ReadLine(); // the MIME line
            for (var scanned = 0; scanned < 8; scanned++)
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

                if (trimmed.StartsWith("resource:", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed["resource:".Length..].Trim();
                    return value.Length == 0 ? null : value;
                }

                // Any other header (body:, view:, the layout: block) means keep scanning only
                // while we are still in the header region; layout: starts the block region.
                if (trimmed.StartsWith("layout:", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
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
