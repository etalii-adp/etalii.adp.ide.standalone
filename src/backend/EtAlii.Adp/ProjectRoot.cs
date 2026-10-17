using IoPath = System.IO.Path;

namespace EtAlii.Adp;

/// <summary>
/// A project's root folder, normalized once (code-structure FR-059): absolute, with no trailing
/// separator, and equal to another root when the file system would call them one folder - without
/// case on Windows and macOS, with case on Linux. A key a store can hold without each caller
/// remembering to normalize, and the one way to ask whether a path is the project's.
/// </summary>
/// <remarks>
/// Normalizing makes the path absolute and nothing more; links are followed where it matters, when
/// <see cref="Contains"/> judges a path, as <see cref="PathContainment"/> always has.
/// </remarks>
public readonly struct ProjectRoot : IEquatable<ProjectRoot>
{
    private ProjectRoot(string fullPath) => FullPath = fullPath;

    /// <summary>The root as an absolute path without a trailing separator, or empty for the default value.</summary>
    public string FullPath { get => field ?? ""; private init; }

    /// <summary>The root <paramref name="path"/> names, made absolute against the current folder when it is relative.</summary>
    /// <exception cref="ArgumentException">The path is empty or not a valid path.</exception>
    public static ProjectRoot From(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return new ProjectRoot(Normalize(path));
    }

    /// <summary>
    /// Whether <paramref name="path"/> is this root or lies inside it, once links are followed, as
    /// <see cref="PathContainment.IsAtOrInside"/> judges.
    /// </summary>
    public bool Contains(string path) => PathContainment.IsAtOrInside(FullPath, path);

    /// <summary>
    /// <paramref name="path"/> - absolute, or relative to this root - as a path within the project,
    /// or null when it leads outside it.
    /// </summary>
    public ProjectPath? Resolve(string path) => ProjectPath.Within(this, path);

    public bool Equals(ProjectRoot other) => PathContainment.NameComparer.Equals(FullPath, other.FullPath);

    public override bool Equals(object? obj) => obj is ProjectRoot other && Equals(other);

    public override int GetHashCode() => PathContainment.NameComparer.GetHashCode(FullPath);

    public override string ToString() => FullPath;

    public static bool operator ==(ProjectRoot left, ProjectRoot right) => left.Equals(right);

    public static bool operator !=(ProjectRoot left, ProjectRoot right) => !left.Equals(right);

    /// <summary>Absolute, without a trailing separator unless the path is a volume's root.</summary>
    internal static string Normalize(string path)
    {
        var full = IoPath.GetFullPath(path);
        var root = IoPath.GetPathRoot(full) ?? "";
        var trimmed = full.TrimEnd(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        return trimmed.Length < root.Length ? root : trimmed;
    }
}

/// <summary>
/// A path within a <see cref="ProjectRoot"/>, normalized once and known to stay inside it
/// (code-structure FR-059): constructed only through <see cref="Within"/>, which refuses a path that
/// leads out of the project, links included, so holding one is the containment check already made.
/// </summary>
public readonly struct ProjectPath : IEquatable<ProjectPath>
{
    private ProjectPath(ProjectRoot root, string fullPath)
    {
        Root = root;
        FullPath = fullPath;
    }

    /// <summary>The project the path is within.</summary>
    public ProjectRoot Root { get; }

    /// <summary>The path, absolute and without a trailing separator.</summary>
    public string FullPath { get => field ?? ""; private init; }

    /// <summary>The path relative to <see cref="Root"/>; <c>.</c> for the root itself.</summary>
    public string RelativePath => IoPath.GetRelativePath(Root.FullPath, FullPath);

    /// <summary>
    /// <paramref name="path"/> - absolute, or relative to <paramref name="root"/> - as a path within it,
    /// or null when it leads outside the project, is not a valid path, or its links cannot be followed.
    /// </summary>
    public static ProjectPath? Within(ProjectRoot root, string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (root.FullPath.Length == 0)
        {
            return null;
        }

        string full;
        try
        {
            full = ProjectRoot.Normalize(IoPath.IsPathRooted(path) ? path : IoPath.Combine(root.FullPath, path));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        return root.Contains(full) ? new ProjectPath(root, full) : null;
    }

    public bool Equals(ProjectPath other) => PathContainment.NameComparer.Equals(FullPath, other.FullPath);

    public override bool Equals(object? obj) => obj is ProjectPath other && Equals(other);

    public override int GetHashCode() => PathContainment.NameComparer.GetHashCode(FullPath);

    public override string ToString() => FullPath;

    public static bool operator ==(ProjectPath left, ProjectPath right) => left.Equals(right);

    public static bool operator !=(ProjectPath left, ProjectPath right) => !left.Equals(right);
}
