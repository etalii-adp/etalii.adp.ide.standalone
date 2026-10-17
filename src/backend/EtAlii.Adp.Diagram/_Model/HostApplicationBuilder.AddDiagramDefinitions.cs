using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Diagram;

public static class HostApplicationBuilderAddDiagramDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Registers the discovered diagram definitions, the diagram catalog over them and each module's own
        /// registrations - the diagram kind's call of <c>AddToolDefinitions</c>, and the one place its
        /// catalog is registered (code-structure FR-057).
        /// </summary>
        public void AddDiagramDefinitions(IReadOnlyList<DiagramDefinition> definitions) =>
            builder.AddToolDefinitions<DiagramDefinition, IDiagramDefinitionCatalog>(definitions, all => new DiagramDefinitionCatalog { All = all });
    }
}
