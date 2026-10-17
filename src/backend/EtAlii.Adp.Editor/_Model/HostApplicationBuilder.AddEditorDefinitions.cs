using Microsoft.Extensions.Hosting;

namespace EtAlii.Adp.Editor;

public static class HostApplicationBuilderAddEditorDefinitionsExtension
{
    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Registers the discovered editor definitions, the editor catalog over them and each module's own
        /// registrations - the editor kind's call of <c>AddToolDefinitions</c>, and the one place its
        /// catalog is registered (code-structure FR-057).
        /// </summary>
        public void AddEditorDefinitions(IReadOnlyList<EditorDefinition> definitions) =>
            builder.AddToolDefinitions<EditorDefinition, IEditorDefinitionCatalog>(definitions, all => new EditorDefinitionCatalog { All = all });
    }
}
