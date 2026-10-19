using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The reload seam of one diagram type: an external write to a body, or its deletion, reaches the
/// module's store, and through it every open session (code-structure FR-028).
/// </summary>
/// <remarks>
/// <para>
/// <b>One generic type in place of a forwarding class per module.</b> Each module had a reloader
/// that differed from the others only in its origin and its store's type; it now registers this one
/// with <see cref="ServiceCollectionAddDiagramDocumentReloaderExtension.AddDiagramDocumentReloader{TStore}"/>.
/// </para>
/// <para>
/// <b><see cref="BodyDeletedAsync"/> is implemented on purpose.</b> The interface's default routes a
/// deletion to a reload, which a store answers by keeping the last good document - so a deleted
/// diagram would stay on the canvas. The store's own deletion is the one call that clears it.
/// <c>DocumentReloaderContract</c> holds every module to that.
/// </para>
/// <para>
/// <b>It lives beside <see cref="IDiagramDocumentReloader"/>, not in <c>EtAlii.Adp.Documents</c></b>
/// where the store interface is: it implements a type of this project, and the documents project
/// does not reference this one.
/// </para>
/// </remarks>
public sealed class DiagramDocumentReloader<TStore> : IDiagramDocumentReloader
    where TStore : IDiagramDocumentStore
{
    private readonly TStore _documents;

    public DiagramDocumentReloader(ToolOrigin origin, TStore documents)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(documents);
        Origin = origin;
        _documents = documents;
    }

    /// <inheritdoc />
    public ToolOrigin Origin { get; }

    /// <inheritdoc />
    public Task ReloadAsync(ProjectRoot root, string bodyPath, CancellationToken cancellationToken)
    {
        _ = root;
        return _documents.ReloadAsync(bodyPath, cancellationToken);
    }

    /// <inheritdoc />
    public Task BodyDeletedAsync(ProjectRoot root, string bodyPath, CancellationToken cancellationToken)
    {
        _ = root;
        return _documents.BodyDeletedAsync(bodyPath, cancellationToken);
    }
}
