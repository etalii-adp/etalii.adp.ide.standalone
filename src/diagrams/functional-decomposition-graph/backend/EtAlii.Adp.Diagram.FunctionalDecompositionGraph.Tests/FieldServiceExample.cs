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
    public static string Path =>
        RepositoryPaths.Under("src", "diagrams", "functional-decomposition-graph", "examples", "field-service", "field-service.fdg");
}
