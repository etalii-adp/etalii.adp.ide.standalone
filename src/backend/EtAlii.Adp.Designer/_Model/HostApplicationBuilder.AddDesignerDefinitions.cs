using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Designer;

public static class HostApplicationBuilderAddDesignerDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Adds the discovered designer definitions and their catalog, and runs each module's
        /// own registrations - the designer family's mirror of <c>AddDiagramDefinitions</c>
        /// and <c>AddEditorDefinitions</c>.
        /// </summary>
        public void AddDesignerDefinitions(IReadOnlyList<DesignerDefinition> definitions)
        {
            ArgumentNullException.ThrowIfNull(definitions);

            builder.Services.AddSingleton(definitions);
            builder.Services.TryAddDesignerDefinitionCatalog();
            foreach (var definition in definitions)
            {
                definition.Build?.Invoke(builder);
            }
        }
    }
}
