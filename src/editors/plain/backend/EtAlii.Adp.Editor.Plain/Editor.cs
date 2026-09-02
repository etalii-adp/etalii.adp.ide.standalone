using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Editor.Plain;

/// <summary>This editor's identity: the fallback, claiming nothing by construction.</summary>
public static class Editor
{
    /// <summary>
    /// This module's one editor, named so its own registrations can say which editor they
    /// serve. Discovery reads <see cref="Definitions"/>; the module reads this.
    /// </summary>
    public static EditorDefinition Plain { get; } = new(
        "plain",
        "Plain Text",
        "Any text file, as it is - the editor every file can fall back to.",
        Icon: "mdi-text-box-outline",
        Extensions: [],
        FileNames: [],
        IsFallback: true,
        Build: builder => builder.Services.AddSingleton<IEditorSessionFactory, PlainEditorSessionFactory>());

    /// <summary>What discovery reads. One entry: this module carries one editor.</summary>
    public static EditorDefinition[] Definitions { get; } = [Plain];
}
