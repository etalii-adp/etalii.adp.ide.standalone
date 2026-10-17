namespace EtAlii.Adp.Diagram;

/// <summary>The registered <see cref="IDiagramSessionFactory"/> instances, looked up by origin.</summary>
public sealed class DiagramSessionFactories
{
    private readonly IReadOnlyDictionary<ToolOrigin, IDiagramSessionFactory> _byOrigin;

    public DiagramSessionFactories(IEnumerable<IDiagramSessionFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _byOrigin = factories.ToDictionary(factory => factory.Origin);
    }

    /// <summary>The factory that opens sessions for <paramref name="origin"/>, or null when the type cannot be streamed.</summary>
    public IDiagramSessionFactory? Find(ToolOrigin origin) =>
        _byOrigin.TryGetValue(origin, out var factory) ? factory : null;
}
