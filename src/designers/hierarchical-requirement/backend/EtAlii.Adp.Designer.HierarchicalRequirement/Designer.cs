using JetBrains.Annotations;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The Hierarchical Requirement Designer: a subject's requirements on one page, items as boxes with their requirements
/// and sub-items to their right, kept in one Markdown file an agent can write too. Found by
/// <see cref="DesignerDefinitionDiscovery"/> through this class's name and its <see cref="Definitions"/>.
/// </summary>
/// <remarks>
/// A requirement document is recognised by the envelope on its first line, with or without a registration (spec 029 R3,
/// R4): the claim is made from the bundled binding alone, whose marker the FBL library's routing answers (spec 093 R1).
/// </remarks>
[UsedImplicitly(ImplicitUseTargetFlags.WithMembers)]
public static class Designer
{
    /// <summary>The designer type's origin, which its envelope names.</summary>
    public const string Origin = HierarchicalRequirementDefinition.Origin;

    public static DesignerDefinition[] Definitions { get; } =
    [
        new(
            Origin,
            HierarchicalRequirementVocabulary.Name,
            "The requirements of a subject on one page, by its structure, kept in a Markdown file an agent can write too.",
            "mdi-file-tree-outline",
            [new DesignerFormat("Markdown", ".md")],
            builder => builder.Services.AddHierarchicalRequirement(),
            new DesignerClaim([HierarchicalRequirementDefinition.Binding])),
    ];
}
