namespace EtAlii.Adp.Diagram.C4;

/// <summary>Every rule's stable id, prefixed with the module's short name as core expects.</summary>
/// <remarks>
/// Which of these mirror a Structurizr rule, and which are ADP's own, is stated in
/// <see cref="C4StructurizrMirror"/> rather than inferred from the names.
/// </remarks>
public static class C4Rules
{
    public const string MissingDescription = "c4.missing-description";
    public const string MissingTechnology = "c4.missing-technology";
    public const string UnlabelledRelationship = "c4.unlabelled-relationship";
    public const string MissingProtocol = "c4.missing-protocol";
    public const string DanglingRelationship = "c4.dangling-relationship";
    public const string KindNotPermitted = "c4.kind-not-permitted-on-view";
    public const string EmptyView = "c4.empty-view";
    public const string UnknownViewScope = "c4.unknown-view-scope";
    public const string MisplacedElement = "c4.misplaced-element";
    public const string IncludeNotFollowed = "c4.include-not-followed";

    // ---- rules that close a gap against Structurizr's own inspector -------------------------

    /// <summary>Mirrors <c>model.deploymentnode.description</c>.</summary>
    public const string MissingDeploymentDescription = "c4.missing-deployment-description";

    /// <summary>Mirrors <c>model.deploymentnode.technology</c>.</summary>
    public const string MissingDeploymentTechnology = "c4.missing-deployment-technology";

    /// <summary>Mirrors <c>model.infrastructurenode.description</c>.</summary>
    public const string MissingInfrastructureDescription = "c4.missing-infrastructure-description";

    /// <summary>Mirrors <c>model.infrastructurenode.technology</c>.</summary>
    public const string MissingInfrastructureTechnology = "c4.missing-infrastructure-technology";

    /// <summary>Mirrors <c>model.element.disconnected</c>.</summary>
    public const string DisconnectedElement = "c4.disconnected-element";

    /// <summary>Mirrors <c>model.element.noview</c>.</summary>
    public const string ElementNotOnAnyView = "c4.element-not-on-any-view";

    /// <summary>Every id above, which the mirror table is asserted to account for in full.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        MissingDescription,
        MissingTechnology,
        UnlabelledRelationship,
        MissingProtocol,
        DanglingRelationship,
        KindNotPermitted,
        EmptyView,
        UnknownViewScope,
        MisplacedElement,
        IncludeNotFollowed,
        MissingDeploymentDescription,
        MissingDeploymentTechnology,
        MissingInfrastructureDescription,
        MissingInfrastructureTechnology,
        DisconnectedElement,
        ElementNotOnAnyView,
    ];
}
