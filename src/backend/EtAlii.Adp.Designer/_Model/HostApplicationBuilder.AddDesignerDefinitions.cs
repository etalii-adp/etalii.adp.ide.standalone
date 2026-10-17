using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Designer;

public static class HostApplicationBuilderAddDesignerDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Registers the discovered designer definitions, the designer catalog over them and each module's own
        /// registrations - the designer kind's call of <c>AddToolDefinitions</c>, and the one place its
        /// catalog is registered (code-structure FR-057).
        /// </summary>
        public void AddDesignerDefinitions(IReadOnlyList<DesignerDefinition> definitions) =>
            builder.AddToolDefinitions<DesignerDefinition, IDesignerDefinitionCatalog>(definitions, all => new DesignerDefinitionCatalog { All = all });
    }
}
