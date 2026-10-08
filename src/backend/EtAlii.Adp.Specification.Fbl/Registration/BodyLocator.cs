using EtAlii.Adp.Specification.Fbl.Documents;

namespace EtAlii.Adp.Specification.Fbl.Registration;

/// <summary>Where a registration's body is (FBL §8.2), or why it is not opened.</summary>
public sealed record BodyLocation(string? Path, bool Exists, string? Refusal)
{
    /// <summary>A missing body opens empty with <c>fbl.missing-body</c>, and nothing is written until the registration names a file.</summary>
    public bool IsMissing => Refusal is null && !Exists;
}

/// <summary>
/// Finds a registration's body (FBL §8.2): the <c>body</c> header relative to the registration's
/// folder, else the sibling with the registration's base name and the first existing claimed
/// extension, else the registration's folder for a folder binding. A body outside the workspace root,
/// or reached through a symbolic link or other reparse point, is refused (FBL §16).
/// </summary>
public static class BodyLocator
{
    public static BodyLocation Locate(string registrationPath, RegistrationDocument registration, FblBinding binding, string workspaceRoot)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(binding);
        var registrationFull = Path.GetFullPath(registrationPath);
        var folder = Path.GetDirectoryName(registrationFull)!;
        var root = Path.GetFullPath(workspaceRoot);
        string candidate;
        if (registration.Body is { } body)
        {
            if (Path.IsPathRooted(body)) return new BodyLocation(null, false, "The registration's body is an absolute path, which is never followed.");
            candidate = Path.GetFullPath(Path.Combine(folder, body.Replace('/', Path.DirectorySeparatorChar)));
        }
        else if (binding.Body.IsFolder)
        {
            candidate = folder;
        }
        else
        {
            var baseName = Path.GetFileNameWithoutExtension(registrationFull);
            candidate = binding.Claims.Extensions
                .Select(e => Path.Combine(folder, baseName + e))
                .FirstOrDefault(File.Exists) ?? Path.Combine(folder, baseName + binding.Claims.Extensions.FirstOrDefault());
        }
        if (!Within(root, candidate)) return new BodyLocation(null, false, "The registration's body is outside the workspace, so it is not opened without the user's consent.");
        if (ThroughReparsePoint(root, candidate)) return new BodyLocation(null, false, "The registration's body is reached through a link, which is not followed without the user's consent.");
        var exists = binding.Body.IsFolder ? Directory.Exists(candidate) : File.Exists(candidate);
        return new BodyLocation(candidate, exists, null);
    }

    private static bool Within(string root, string path)
    {
        var comparison = Routing.Glob.PlatformIgnoresCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(path, root, comparison) || path.StartsWith(prefix, comparison);
    }

    /// <summary>Whether any existing component from below the root to the path itself is a reparse point.</summary>
    private static bool ThroughReparsePoint(string root, string path)
    {
        for (var current = path; current.Length > root.Length; current = Path.GetDirectoryName(current)!)
        {
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        }
        return false;
    }
}
