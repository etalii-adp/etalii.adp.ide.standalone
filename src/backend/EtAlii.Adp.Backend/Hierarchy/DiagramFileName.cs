using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// How a diagram file is named: the extension every ADP-created diagram carries, and the
/// name suggested for a chosen diagram type in a given folder. The suggestion is a starting
/// point the user is free to replace - what makes it useful is that pressing Add without
/// typing anything still produces a readable, free name.
/// </summary>
public static class DiagramFileName
{
    public const string Extension = ".adp";

    /// <summary>
    /// A free name for a new diagram of this type in <paramref name="folder"/>: the origin's
    /// type segment (plus its subtype), and a number when that is already taken. Only the
    /// base name is returned - <see cref="WithExtension"/> adds the extension.
    /// </summary>
    public static string Suggest(DiagramOrigin origin, string folder)
    {
        var baseName = Sanitise(origin.Subtype.Length == 0 ? origin.Type : $"{origin.Type}-{origin.Subtype}");

        if (IsFree(folder, baseName))
        {
            return baseName;
        }

        // Counting up from 2 reads the way a person would name the second one; stopping at the
        // first free candidate keeps this a handful of existence checks, never a folder listing.
        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = $"{baseName}-{suffix}";
            if (IsFree(folder, candidate))
            {
                return candidate;
            }
        }

        return baseName; // unreachable in practice; the caller's validation refuses a taken name
    }

    /// <summary>
    /// Drops one trailing <c>.adp</c> if the user typed it, so naming a diagram "domain.adp"
    /// means the same file as naming it "domain" rather than "domain.adp.adp".
    /// </summary>
    public static string StripExtension(string typedName)
    {
        var name = typedName.Trim();
        return name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)
            ? name[..^Extension.Length]
            : name;
    }

    /// <summary>The file name for a base name: always exactly one <c>.adp</c>.</summary>
    public static string WithExtension(string baseName) => StripExtension(baseName) + Extension;

    private static bool IsFree(string folder, string baseName)
    {
        var path = IoPath.Combine(folder, WithExtension(baseName));
        return !File.Exists(path) && !Directory.Exists(path);
    }

    /// <summary>
    /// Replaces anything a file name cannot carry with a hyphen, so no origin - whose segments
    /// this spec does not control - can suggest a name that reaches out of its folder or that
    /// the filesystem would refuse. Runs of hyphens collapse so the result stays readable.
    /// </summary>
    private static string Sanitise(string value)
    {
        var invalid = IoPath.GetInvalidFileNameChars();
        var characters = value.Select(character => invalid.Contains(character) || character is '.' or ' ' ? '-' : character);

        var sanitised = new string(characters.ToArray());
        while (sanitised.Contains("--", StringComparison.Ordinal))
        {
            sanitised = sanitised.Replace("--", "-", StringComparison.Ordinal);
        }

        sanitised = sanitised.Trim('-');
        return sanitised.Length == 0 ? "diagram" : sanitised;
    }
}
