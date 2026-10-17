namespace EtAlii.Adp.Sessions;

/// <summary>
/// The registered session factories of one tool kind, looked up by the key each factory serves -
/// the one registry of the three kinds (code-structure FR-057). A kind names its own registry by
/// deriving from this one with the key its factories carry: a diagram's origin, a designer's
/// origin, an editor's id. Two factories serving one key are a fault the registry refuses.
/// </summary>
/// <typeparam name="TKey">What a factory serves and is looked up by.</typeparam>
/// <typeparam name="TFactory">The kind's factory contract.</typeparam>
public class ToolSessionFactories<TKey, TFactory>
    where TKey : notnull
    where TFactory : class
{
    private readonly IReadOnlyDictionary<TKey, TFactory> _byKey;

    /// <param name="factories">The factories the modules registered.</param>
    /// <param name="keyOf">The key a factory serves.</param>
    /// <param name="comparer">How two keys compare, or null for the key type's own equality.</param>
    protected ToolSessionFactories(IEnumerable<TFactory> factories, Func<TFactory, TKey> keyOf, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(factories);
        ArgumentNullException.ThrowIfNull(keyOf);
        _byKey = factories.ToDictionary(keyOf, comparer);
    }

    /// <summary>The factory serving <paramref name="key"/>, or null when no module registered one.</summary>
    public TFactory? Find(TKey key) => _byKey.GetValueOrDefault(key);
}
