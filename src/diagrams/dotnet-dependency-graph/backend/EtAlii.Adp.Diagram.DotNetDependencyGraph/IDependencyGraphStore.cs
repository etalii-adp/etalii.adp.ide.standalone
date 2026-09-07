namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// Holds the derived graph for a diagram, so that the property provider and the session read
/// one graph rather than each deriving its own.
/// </summary>
/// <remarks>
/// The seam exists at task 6 so the property provider can be built and tested against a graph
/// without a filesystem, a session or a watcher behind it; task 7 supplies the implementation
/// that actually reads, caches and refreshes.
/// </remarks>
public interface IDependencyGraphStore
{
    /// <summary>
    /// The graph for the diagram registered at <paramref name="diagramPath"/>, derived if it
    /// has not been derived yet. Never throws for an unreadable subject: an empty graph
    /// carrying its failures is the answer, because the diagram opens either way.
    /// </summary>
    DependencyGraphModel GetOrLoad(string diagramPath);
}
