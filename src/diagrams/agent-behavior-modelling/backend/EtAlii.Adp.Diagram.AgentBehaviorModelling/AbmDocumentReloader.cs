using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The reload seam: an external write to an Markdown body reaches the store, and through it
/// every open session.
/// </summary>
/// <remarks>
/// <b><see cref="BodyDeleted"/> IS OVERRIDDEN, AND DROPPING THE OVERRIDE LOOKS HARMLESS AND IS NOT.</b>
/// <see cref="IDiagramDocumentReloader.BodyDeleted"/> defaults to a reload, which was right while a
/// read that failed installed an empty document. This store keeps the last good document through a
/// reload that cannot read. So a deletion routed to a reload finds nothing to read, keeps the last
/// good document, and a deleted diagram stays on the canvas for good, while the type system says
/// nothing. The lifecycle's own <c>BodyDeleted</c> is the one call that clears it. The guard is
/// <c>AbmDocumentReloader.Tests</c>, seen to fail with this override removed.
/// </remarks>
public sealed class AbmDocumentReloader : IDiagramDocumentReloader
{
    private readonly IAbmDocumentStore _documents;

    public AbmDocumentReloader(IAbmDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin => Diagram.AgentBehaviorModelling.Origin;

    /// <inheritdoc />
    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        _documents.Reload(bodyPath);
    }

    /// <inheritdoc />
    public void BodyDeleted(string rootPath, string bodyPath)
    {
        _ = rootPath;
        _documents.BodyDeleted(bodyPath);
    }
}
