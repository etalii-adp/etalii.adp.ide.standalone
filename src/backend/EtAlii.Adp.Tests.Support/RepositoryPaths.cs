using IoPath = System.IO.Path;

namespace EtAlii.Adp.Tests.Support;

/// <summary>
/// The folders of this repository a test reads from, found by walking up from the test binary.
/// </summary>
/// <remarks>
/// One locator rather than one per test class: the copies each recognised the root by a different
/// pair of folders, so a move broke them one at a time. Over forty such copies - a root, a module
/// folder, an examples folder, each walking up from the test binary on its own - now ask here. The root is the first folder upwards that
/// holds <c>src/backend</c> beside <c>src/diagrams</c>.
/// </remarks>
public static class RepositoryPaths
{
    private static readonly Lazy<string> _root = new(Locate);

    /// <summary>The repository root.</summary>
    public static string Root => _root.Value;

    /// <summary><c>src/</c>.</summary>
    private static string Source => IoPath.Combine(Root, "src");

    /// <summary><c>src/fixtures/</c>, the test data both suites read.</summary>
    public static string Fixtures => IoPath.Combine(Source, "fixtures");

    /// <summary>
    /// The folder or file at <paramref name="segments"/> under the root. One that is not there
    /// throws rather than letting a test pass over nothing: a moved folder is a failure, not a skip.
    /// </summary>
    public static string Under(params string[] segments)
    {
        var path = IoPath.Combine([Root, .. segments]);
        return Directory.Exists(path) || File.Exists(path)
            ? path
            : throw new DirectoryNotFoundException($"{string.Join('/', segments)} was not found under the repository root {Root}.");
    }

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "backend")) &&
                Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/backend beside src/diagrams) was not found above the test binary.");
    }
}
