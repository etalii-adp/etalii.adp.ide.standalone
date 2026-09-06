using IoPath = System.IO.Path;

namespace EtAlii.Adp.Projects;

public static class PathRecordExtensions
{
    /// <summary>
    /// The absolute folder path a wire path stands for. The wire chops an absolute path into
    /// segments, and that representation cannot carry the Unix root: <c>/tmp/x</c> arrives as
    /// <c>["tmp","x"]</c> and recombines relative, which is how every project-adding
    /// integration test failed on the first Linux CI run while Windows - whose root lives
    /// inside its own <c>"C:"</c> segment - never noticed. A recombined path that is not
    /// rooted gets the platform root put back; a rooted one is returned untouched.
    /// </summary>
    public static string AbsolutePath(this IReadOnlyList<string> segments)
    {
        var combined = IoPath.Combine([.. segments]);
        return IoPath.IsPathRooted(combined) ? combined : IoPath.DirectorySeparatorChar + combined;
    }
}
