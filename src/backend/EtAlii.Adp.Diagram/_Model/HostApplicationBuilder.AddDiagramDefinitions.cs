using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Diagram;

public static class HostApplicationBuilderAddDiagramDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Registers the diagram definitions as a singleton and runs each one's <c>Build</c> hook.
        /// </summary>
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
