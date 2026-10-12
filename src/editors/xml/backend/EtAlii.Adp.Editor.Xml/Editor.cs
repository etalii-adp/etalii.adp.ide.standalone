using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>This editor's identity: XML and its two closest relatives, by extension.</summary>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Editor
{
    /// <summary>
    /// This module's one editor, named so its own registrations can say which editor they
    /// serve. Discovery reads <see cref="Definitions"/>; the module reads this.
    /// </summary>
    public static EditorDefinition Xml { get; } = new(
        "xml",
        "XML editor",
        "XML documents, schemas and stylesheets, coloured, checked as you type, and completed from the schema a document names.",
        Icon: "mdi-xml",
        Extensions: [".xml", ".xsd", ".xsl", ".xslt"],
        FileNames: [],
        Build: builder => builder.Services.AddSingleton<IEditorSessionFactory, XmlEditorSessionFactory>());

    /// <summary>What discovery reads. One entry: this module carries one editor.</summary>
    public static EditorDefinition[] Definitions { get; } = [Xml];
}
