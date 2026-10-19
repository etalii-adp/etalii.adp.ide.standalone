using System.Globalization;

namespace EtAlii.Adp.Specification.Json;

/// <summary>
/// The version of a specification language a loader reads: the highest major it supports and the
/// highest minor of that major it knows. Both specifications write a version the same way (DISL §2.9,
/// FBL §2.7): exactly <c>major.minor</c> in digits, a higher major refused, a newer minor read with a
/// warning. The check reports what it finds and leaves the loader to go on with the rest of the document.
/// </summary>
/// <param name="Language">The language's name as a diagnostic gives it: <c>DISL</c>, <c>FBL</c>.</param>
/// <param name="Section">The section that says how its version is written, as a diagnostic cites it.</param>
public sealed record LanguageVersion(string Language, int Major, int Minor, string Section)
{
    /// <summary>What <paramref name="version"/>, found at <paramref name="pointer"/>, says about reading the document; null when it is read as it is.</summary>
    public LoadDiagnostic? Check(string version, string pointer)
    {
        ArgumentNullException.ThrowIfNull(version);
        var parts = version.Split('.');
        if (parts.Length != 2 || !TryDigits(parts[0], out var major) || !TryDigits(parts[1], out var minor))
        {
            return new LoadDiagnostic(pointer, LoadSeverity.Error, $"'{version}' is not a {Language} version written as major.minor ({Section}).");
        }
        if (major > Major)
        {
            return new LoadDiagnostic(pointer, LoadSeverity.Error, $"This runtime reads {Language} {Major}.x; {Language} {version} is refused ({Section}).");
        }
        return major == Major && minor > Minor
            ? new LoadDiagnostic(pointer, LoadSeverity.Warning, $"This runtime knows {Language} up to {Major}.{Minor}; what {Language} {version} adds is not read ({Section}).")
            : null;
    }

    /// <summary>Digits only: no sign, no space, no separator.</summary>
    private static bool TryDigits(string text, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
