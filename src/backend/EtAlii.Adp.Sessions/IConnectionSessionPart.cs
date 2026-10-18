namespace EtAlii.Adp.Sessions;

/// <summary>
/// One thing a connection holds of its own - the hierarchy model of its watch - acquired by its
/// <see cref="ConnectionSession"/> and released with it.
/// </summary>
/// <remarks>
/// Declared here and implemented by the project that owns the part, as <see cref="IProjectSessionPart"/> is.
/// </remarks>
public interface IConnectionSessionPart
{
    /// <summary>Acquires the part for the connection watching <paramref name="root"/> as <paramref name="watchId"/>; disposing what is returned releases it.</summary>
    IDisposable Acquire(ShortGuid watchId, ProjectRoot root);
}
