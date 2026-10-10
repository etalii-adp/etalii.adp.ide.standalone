namespace EtAlii.Adp.Designer.Knowledge;

/// <summary>
/// The Knowledge designer: a table of rows and typed properties, with views of it, kept in one
/// plain YAML, JSON or XML file. Found by <see cref="DesignerDefinitionDiscovery"/> through this
/// class's name and its <see cref="Definitions"/>.
/// </summary>
public static class Designer
{
    /// <summary>The designer type's origin: the first line of a knowledge file's registration.</summary>
    public const string Origin = "etalii/knowledge";

    public static DesignerDefinition[] Definitions { get; } =
    [
        new(
            Origin,
            "Knowledge",
            "A table of rows and typed properties, with views of it, kept in a plain text file you own.",
            "mdi-table",
            // The order they are offered in when a table is added. The format is chosen then and never changed.
            [new DesignerFormat("YAML", ".yaml"), new DesignerFormat("JSON", ".json"), new DesignerFormat("XML", ".xml")],
            builder => builder.Services.AddKnowledge()),
    ];
}
