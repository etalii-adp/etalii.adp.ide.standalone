using EtAlii.Adp.Sessions;
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Editor.Json;

/// <summary>This editor's identity: JSON, by extension (json-editor Requirement 2.1).</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Editor
{
    /// <summary>
    /// This module's one editor, named so its own registrations can say which editor they
    /// serve. Discovery reads <see cref="Definitions"/>; the module reads this.
    /// </summary>
    /// <remarks>
    /// <c>.json</c> only (ruling Q1): <c>.jsonc</c>, <c>.json5</c> and the rest allow what this
    /// editor reports as a mistake. A <c>.json</c> file that is a diagram's body still opens as the
    /// diagram, because diagrams are resolved first and the Databricks pipeline claims its
    /// <c>.json</c> through a registration only.
    /// </remarks>
    public static EditorDefinition Json { get; } = new(
        "json",
        "JSON editor",
        "JSON files, edited as text - coloured, checked against the JSON Schema their $schema names, and completed from it.",
        Icon: "mdi-code-json",
        Extensions: [".json"],
        FileNames: [],
        Build: builder => builder.Services.AddSingleton<IEditorSessionFactory, JsonEditorSessionFactory>());

    /// <summary>What discovery reads. One entry: this module carries one editor.</summary>
    public static EditorDefinition[] Definitions { get; } = [Json];
}
