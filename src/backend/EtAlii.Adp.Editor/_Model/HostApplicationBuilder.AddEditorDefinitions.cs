using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Editor;

public static class HostApplicationBuilderAddEditorDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Adds the discovered editor definitions and runs each module's own registrations -
        /// the editor family's mirror of <c>AddDiagramDefinitions</c>.
        /// </summary>
        public void AddEditorDefinitions(IReadOnlyList<EditorDefinition> definitions)
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
