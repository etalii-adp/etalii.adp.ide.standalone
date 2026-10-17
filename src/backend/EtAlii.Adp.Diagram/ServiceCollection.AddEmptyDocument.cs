using EtAlii.Adp.Documents;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Diagram;

public static class ServiceCollectionAddEmptyDocumentExtension
{
    /// <summary>
    /// Registers <paramref name="text"/> as the new, empty document of the type at
    /// <paramref name="origin"/>, which the Add dialog writes and without which the host does not
    /// start for a type that declares an extension.
    /// </summary>
    public static void AddEmptyDocument(this IServiceCollection services, ToolOrigin origin, string text)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(text);

        services.AddSingleton<IDiagramDocumentFactory>(new EmptyDocumentFactory(origin, text));
    }

    /// <summary>
    /// Registers <paramref name="textFromBaseName"/> as what writes the new, empty document of the
    /// type at <paramref name="origin"/> from the file's base name - a map titled after its file,
    /// a starter named after it - for a type whose new document is not one fixed text.
    /// </summary>
    public static void AddEmptyDocument(this IServiceCollection services, ToolOrigin origin, Func<string, string> textFromBaseName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(textFromBaseName);

        services.AddSingleton<IDiagramDocumentFactory>(new EmptyDocumentFactory(origin, textFromBaseName));
    }
}
