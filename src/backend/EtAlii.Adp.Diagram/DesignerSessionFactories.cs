using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The registered designer session factories by designer type: what the table calls ask for the
/// session of a document, without knowing any designer type.
/// </summary>
public sealed class DesignerSessionFactories
{
    private readonly IReadOnlyDictionary<string, IDesignerSessionFactory> _byOrigin;

    public DesignerSessionFactories(IEnumerable<IDesignerSessionFactory> factories)
    {
        ArgumentNullException.ThrowIfNull(factories);
        _byOrigin = factories.ToDictionary(factory => factory.Origin, StringComparer.Ordinal);
    }

    /// <summary>The factory of the designer type <paramref name="origin"/>, or null when its module registered none.</summary>
    public IDesignerSessionFactory? Find(string origin) => _byOrigin.GetValueOrDefault(origin);
}
