namespace EtAlii.Adp.Common;

/// <summary>
/// The registered <see cref="IDiagramDocumentFactory"/> instances, looked up by origin, and
/// the startup check that every definition needing one has one.
/// </summary>
public sealed class DiagramDocumentFactories
{
    private readonly IReadOnlyDictionary<DiagramOrigin, IDiagramDocumentFactory> _byOrigin;

    public DiagramDocumentFactories(IEnumerable<IDiagramDocumentFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _byOrigin = factories.ToDictionary(factory => factory.Origin);
    }

    /// <summary>The factory for <paramref name="origin"/>, or null when the type keeps no sibling.</summary>
    public IDiagramDocumentFactory? Find(DiagramOrigin origin) => _byOrigin.TryGetValue(origin, out var factory) ? factory : null;

    /// <summary>
    /// Every definition in <paramref name="definitions"/> that declares an extension but has
    /// no factory to write its body - the deployment errors the host reports at startup so
    /// they cannot surface as a failed Add later. Empty means the deployment is consistent.
    /// </summary>
    public IReadOnlyList<DiagramDefinition> Verify(IEnumerable<DiagramDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        return definitions
            .Where(definition => definition.HasDocumentSibling && !_byOrigin.ContainsKey(definition.Origin))
            .ToArray();
    }
}
