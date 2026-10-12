using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram;

public static class ServiceCollectionAddDiagramSessionExtension
{
    /// <summary>
    /// Registers how a session on a diagram of the type at <paramref name="origin"/> is opened:
    /// <paramref name="open"/> is handed the provider and what the session is opened on.
    /// </summary>
    public static void AddDiagramSession(this IServiceCollection services, DiagramOrigin origin, Func<IServiceProvider, DiagramSessionOpening, IDiagramSession> open)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(open);

        services.AddSingleton<IDiagramSessionFactory>(provider => new DiagramSessionFactory(origin, provider, open));
    }

    /// <summary>
    /// Registers <paramref name="text"/> as the new, empty document of the type at
    /// <paramref name="origin"/>, which the Add dialog writes and without which the host does not
    /// start for a type that declares an extension.
    /// </summary>
    public static void AddEmptyDocument(this IServiceCollection services, DiagramOrigin origin, string text)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(text);

        services.AddSingleton<IDiagramDocumentFactory>(new EmptyDocumentFactory(origin, text));
    }
}
