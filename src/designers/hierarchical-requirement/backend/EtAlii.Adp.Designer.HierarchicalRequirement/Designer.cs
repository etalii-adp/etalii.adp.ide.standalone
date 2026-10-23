using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Designer.HierarchicalRequirement;

/// <summary>
/// The Hierarchical Requirement Designer: a subject's requirements on one page, items as boxes with their requirements
/// and sub-items to their right, kept in one Markdown file an agent can write too. Found by
/// <see cref="DesignerDefinitionDiscovery"/> through this class's name and its <see cref="Definitions"/>.
/// </summary>
/// <remarks>
/// A requirement document is recognised by the envelope on its first line, with or without a registration (spec 029 R3,
/// R4): the claim is the binding's own marker, answered by the FBL library's routing.
/// </remarks>
public static class Designer
{
    /// <summary>The designer type's origin, which its envelope names.</summary>
    public const string Origin = HierarchicalRequirementDefinition.Origin;

    /// <summary>The line a requirement document carries on its first line, for a refusal to name.</summary>
    private const string Envelope = "<!-- ded: 0.2 designer: " + Origin + " -->";

    public static DesignerDefinition[] Definitions { get; } =
    [
        new(
            Origin,
            HierarchicalRequirementVocabulary.Name,
            "The requirements of a subject on one page, by its structure, kept in a Markdown file an agent can write too.",
            "mdi-file-tree-outline",
            [new DesignerFormat("Markdown", ".md")],
            builder => builder.Services.AddHierarchicalRequirement(),
            new DesignerClaim(
                [".md"],
                (name, head) => BodyClaim.Claims(HierarchicalRequirementDefinition.Binding, name, head),
                Envelope,
                BodyClaim.CreatesRegistration(HierarchicalRequirementDefinition.Binding))),
    ];
}
