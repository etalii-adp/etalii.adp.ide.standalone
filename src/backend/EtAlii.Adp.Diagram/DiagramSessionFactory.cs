using EtAlii.Adp.Documents;
using JetBrains.Annotations;

namespace EtAlii.Adp.Diagram;

// Modules read the members their session needs: supply-chain the watch id, c4 the registration.
/// <summary>What a session is opened on: the connection, the project, the body and the registration it was opened through.</summary>
/// <param name="WatchId">The connection that opened the session.</param>
/// <param name="RootPath">The root path of the project.</param>
/// <param name="BodyPath">The path to the diagram body file.</param>
/// <param name="RegistrationPath">The <c>.adp</c> file it was opened through, or null for a body opened without one.</param>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public sealed record DiagramSessionOpening(ShortGuid WatchId, string RootPath, string BodyPath, string? RegistrationPath);

/// <summary>
/// An <see cref="IDiagramSessionFactory"/> made from a delegate, so a module registers how its
/// session is opened rather than writing a class to say it (code-structure FR-030).
/// </summary>
/// <remarks>
/// Seventeen of twenty-one session factories were a constructor that stored services and an
/// <c>Open</c> that passed them to the session; the delegate resolves the same services from the
/// provider when a session is opened. <see cref="ServiceCollectionAddDiagramSessionExtension.AddDiagramSession"/>
/// registers one.
/// </remarks>
public sealed class DiagramSessionFactory : IDiagramSessionFactory
{
    private readonly IServiceProvider _services;
    private readonly Func<IServiceProvider, DiagramSessionOpening, IDiagramSession> _open;

    public DiagramSessionFactory(DiagramOrigin origin, IServiceProvider services, Func<IServiceProvider, DiagramSessionOpening, IDiagramSession> open)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(open);
        Origin = origin;
        _services = services;
        _open = open;
    }

    /// <inheritdoc />
    public DiagramOrigin Origin { get; }

    /// <inheritdoc />
    public IDiagramSession Open(ShortGuid watchId, string rootPath, string bodyPath, string? registrationPath) =>
        _open(_services, new DiagramSessionOpening(watchId, rootPath, bodyPath, registrationPath));
}
