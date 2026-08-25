namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// What a C4 view offers in the Toolbox: exactly the element kinds that view permits, and
/// nothing it would then refuse. Described by the backend as data - the client renders a
/// palette it does not interpret (c4-diagrams Requirement 12).
/// </summary>
/// <remarks>
/// One provider per type, because the permitted kinds are what differ between the seven. The
/// panel that renders this does not exist yet; the type still states what it contributes, per
/// tech.md's instruction that a spec covers the toolbox even when its host arrives separately
/// (Requirement 12.5).
/// </remarks>
public sealed class C4ToolboxProvider : IDiagramToolboxProvider
{
    private readonly C4ViewKind _viewKind;

    public C4ToolboxProvider(DiagramOrigin origin, C4ViewKind viewKind)
    {
        ArgumentNullException.ThrowIfNull(origin);
        Origin = origin;
        _viewKind = viewKind;
        Items = ItemsFor(viewKind);
    }

    public DiagramOrigin Origin { get; }

    public IReadOnlyList<ToolboxItemDefinition> Items { get; }

    /// <summary>The kinds this view permits, as palette entries. Drawn from the same rule the validator uses, so the two cannot disagree.</summary>
    private static IReadOnlyList<ToolboxItemDefinition> ItemsFor(C4ViewKind viewKind) =>
        C4RuleSet.PermittedKinds(viewKind)
            .Select(kind => new ToolboxItemDefinition(
                $"c4.toolbox.{kind.ToString().ToLowerInvariant()}",
                Label(kind),
                IconFor(kind),
                DescriptionFor(kind, viewKind),
                $"c4.add-{kind.ToString().ToLowerInvariant()}"))
            .ToArray();

    private static string Label(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "Software System",
        C4ElementKind.DeploymentNode => "Deployment Node",
        C4ElementKind.InfrastructureNode => "Infrastructure Node",
        C4ElementKind.ContainerInstance => "Container Instance",
        C4ElementKind.SoftwareSystemInstance => "Software System Instance",
        _ => kind.ToString(),
    };

    private static string IconFor(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Person => "mdi-account-outline",
        C4ElementKind.SoftwareSystem or C4ElementKind.SoftwareSystemInstance => "mdi-application-outline",
        C4ElementKind.Container or C4ElementKind.ContainerInstance => "mdi-package-variant-closed",
        C4ElementKind.Component => "mdi-puzzle-outline",
        C4ElementKind.DeploymentNode => "mdi-server",
        C4ElementKind.InfrastructureNode => "mdi-lan",
        _ => "mdi-shape-outline",
    };

    private static string DescriptionFor(C4ElementKind kind, C4ViewKind viewKind) => kind switch
    {
        // A container is an application or a data store, and saying so matters: people read
        // "container" as Docker, which C4 explicitly does not mean (Requirement 6.3).
        C4ElementKind.Container => "An application or a data store - not a Docker container.",
        C4ElementKind.Component => "A grouping of related functionality inside a container.",
        C4ElementKind.Person => "Someone who uses the software.",
        C4ElementKind.SoftwareSystem => viewKind is C4ViewKind.SystemContext
            ? "Another system this one talks to."
            : "A software system.",
        C4ElementKind.DeploymentNode => "Where things run: a server, a cluster, an execution environment.",
        C4ElementKind.InfrastructureNode => "Supporting infrastructure: DNS, a load balancer, a firewall.",
        C4ElementKind.ContainerInstance => "A container, deployed onto a node.",
        _ => Label(kind),
    };
}
