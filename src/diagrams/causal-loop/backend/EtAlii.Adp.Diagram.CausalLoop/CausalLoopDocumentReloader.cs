using EtAlii.Adp.Backend.Diagrams;
using EtAlii.Adp.Common;

namespace EtAlii.Adp.Diagram.CausalLoop;

/// <summary>
/// The reload seam: a <c>.cld</c> changed on disk - by a text editor, a branch switch, or this
/// module's own writer - is re-read here and the open sessions told.
/// </summary>
public sealed class CausalLoopDocumentReloader(DiagramOrigin origin, ICausalLoopDocumentStore documents)
    : IDiagramDocumentReloader
{
    /// <inheritdoc />
    public DiagramOrigin Origin { get; } = origin;

    /// <inheritdoc />
    public void Reload(string rootPath, string bodyPath)
    {
        _ = rootPath;
        documents.Reload(bodyPath);
    }
}
