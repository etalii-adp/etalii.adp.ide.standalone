using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp;

public static class HostApplicationBuilderAddToolDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// The one registration of the three tool kinds (code-structure FR-057): the discovered
        /// definitions, the kind's catalog over them - registered here and nowhere else - and each
        /// module's own registrations. TryAdd on the catalog, so a test that registered its own list
        /// first keeps it.
        /// </summary>
        /// <param name="definitions">What discovery found.</param>
        /// <param name="catalog">The kind's catalog over those definitions.</param>
        public void AddToolDefinitions<T, TCatalog>(IReadOnlyList<T> definitions, Func<IReadOnlyList<T>, TCatalog> catalog)
            where T : IToolDefinition
            where TCatalog : class, IToolDefinitionCatalog<T>
        {
            ArgumentNullException.ThrowIfNull(definitions);
            ArgumentNullException.ThrowIfNull(catalog);

            builder.Services.AddSingleton(definitions);
            builder.Services.TryAddSingleton(catalog(definitions));
            foreach (var definition in definitions)
            {
                definition.Build?.Invoke(builder);
            }
        }
    }
}
