using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Editor.Markdown;

/// <summary>This editor's identity: Markdown, by extension.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Editor
{
    /// <summary>
    /// This module's one editor, named so its own registrations can say which editor they
    /// serve. Discovery reads <see cref="Definitions"/>; the module reads this.
    /// </summary>
    public static EditorDefinition Markdown { get; } = new(
        "markdown",
        "Markdown",
        "Markdown documents, edited as text - with the preview and heading navigation that make them worth their own editor.",
        Icon: "mdi-language-markdown-outline",
        Extensions: [".md", ".markdown"],
        FileNames: [],
        Build: builder => builder.Services.AddSingleton<IEditorSessionFactory, MarkdownEditorSessionFactory>());

    /// <summary>What discovery reads. One entry: this module carries one editor.</summary>
    public static EditorDefinition[] Definitions { get; } = [Markdown];
}
