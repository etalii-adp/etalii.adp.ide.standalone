using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Diagram;

public static class HostApplicationBuilderAddDiagramDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Builds and adds the diagram definitions.
        /// </summary>
        /// <returns><c>true</c> when this call ran the scan and filled the cache; <c>false</c> when it was already filled.</returns>
        public void AddDiagramDefinitions(IReadOnlyList<DiagramDefinition> definitions)
        {
            ArgumentNullException.ThrowIfNull(definitions);

            builder.Services.AddSingleton(definitions);
            foreach (var definition in definitions)
            {
                definition.Build?.Invoke(builder);
            }
        }
    }
}
