namespace EtAlii.Adp.Diagram;

/// <summary>The registered <see cref="IDiagramSessionFactory"/> instances, looked up by origin; null when the type cannot be streamed.</summary>
public sealed class DiagramSessionFactories(IEnumerable<IDiagramSessionFactory> factories)
    : ToolSessionFactories<ToolOrigin, IDiagramSessionFactory>(factories, factory => factory.Origin);
