using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Workspace;

/// <summary>
/// The registered designer session factories by designer type: what the table calls ask for the
/// session of a document, without knowing any designer type; null when its module registered none.
/// </summary>
public sealed class DesignerSessionFactories(IEnumerable<IDesignerSessionFactory> factories)
    : ToolSessionFactories<string, IDesignerSessionFactory>(factories, factory => factory.Origin, StringComparer.Ordinal);
