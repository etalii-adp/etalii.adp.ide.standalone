namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests;

/// <summary>Where the module's own field-service example is, for every test that reads it.</summary>
/// <remarks>
/// <b>A missing example FAILS rather than skips.</b> The shape other modules use - skip when no
/// <c>examples</c> folder is found - reads as green in a gate, and its upward walk stops at the
/// first folder holding ANY <c>examples</c> directory, so a module without its own would climb to
/// <c>src/examples</c> and find the wrong corpus while reporting success. This anchors on this
/// module's own folder by name and throws when it is not there.
/// </remarks>
internal static class FieldServiceExample
{
    /// <summary>The example's <c>.fdg</c> body.</summary>
    public static string Path
    {
        get
        {
            // Up out of bin/Debug/net10.0 to this module's own folder, by name - not to the first
            // folder that happens to contain an examples directory.
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (directory.Name == "functional-decomposition-graph")
                {
                    var path = System.IO.Path.Combine(directory.FullName, "examples", "field-service", "field-service.fdg");
                    return !File.Exists(path)
                        ? throw new InvalidOperationException($"The field-service example is missing: {path}")
                        : path;
                }
            }

            throw new InvalidOperationException(
                $"No functional-decomposition-graph folder above {AppContext.BaseDirectory}, so the example cannot be found. That is a failure, not a skip.");
        }
    }
}
