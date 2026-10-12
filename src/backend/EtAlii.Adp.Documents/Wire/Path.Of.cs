namespace EtAlii.Adp.Documents.Wire;

// The generated message has only a parameterless constructor and a read-only Segments list, so
// every caller wrote the same three lines to build one; tests alone did it over seventy times.
public sealed partial class Path
{
    /// <summary>A path of <paramref name="segments"/>, in order.</summary>
    public static Path Of(params IEnumerable<string> segments)
    {
        var path = new Path();
        path.Segments.AddRange(segments);
        return path;
    }

    /// <summary>
    /// A path of the segments of a file-system <paramref name="location"/>, split on either
    /// separator, with the empty segments a leading or doubled separator leaves dropped.
    /// </summary>
    public static Path OfLocation(string location) =>
        Of(location.Split([System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
}
