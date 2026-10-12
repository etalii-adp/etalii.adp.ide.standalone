using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Editor.Yaml;

/// <summary>This editor's identity: YAML, by extension.</summary>
/// <remarks>
/// A YAML file that is a diagram's body still opens its diagram: the resolver asks editors only
/// when no diagram claims the file, and "Open as text" asks for the editor by extension
/// (yaml-editor Requirement 2.3). Nothing YAML-specific happens on the backend: what YAML is lives
/// in the module's client, and <c>YamlDotNet</c> is not referenced.
/// </remarks>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Editor
{
    /// <summary>
    /// This module's one editor, named so its own registrations can say which editor they
    /// serve. Discovery reads <see cref="Definitions"/>; the module reads this.
    /// </summary>
    public static EditorDefinition Yaml { get; } = new(
        "yaml",
        "YAML editor",
        "YAML files, edited as text - with colour, indentation kept in spaces, mistakes marked where they are made, and the keys and values a named schema allows.",
        Icon: "mdi-code-braces-box",
        Extensions: [".yaml", ".yml"],
        FileNames: [],
        Build: builder => builder.Services.AddSingleton<IEditorSessionFactory, YamlEditorSessionFactory>());

    /// <summary>What discovery reads. One entry: this module carries one editor.</summary>
    public static EditorDefinition[] Definitions { get; } = [Yaml];
}
