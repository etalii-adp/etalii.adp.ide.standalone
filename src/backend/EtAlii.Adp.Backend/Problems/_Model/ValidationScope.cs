namespace EtAlii.Adp.Backend.Problems;

/// <summary>
/// What one validation request covers: a single diagram, a folder and everything beneath it,
/// or the whole project. Paths are project-relative; the root anchors them.
/// </summary>
public abstract record ValidationScope(string RootPath)
{
    /// <summary>One diagram file, named relative to the root.</summary>
    public sealed record File(string RootPath, string RelativePath) : ValidationScope(RootPath);

    /// <summary>One folder and everything beneath it, named relative to the root.</summary>
    public sealed record Folder(string RootPath, string RelativePath) : ValidationScope(RootPath);

    /// <summary>The whole project - Validate all.</summary>
    public sealed record Project(string RootPath) : ValidationScope(RootPath);
}
