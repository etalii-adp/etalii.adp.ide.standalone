namespace EtAlii.Adp.Diagram.C4;

/// <summary>The C4 abstraction an element is. The hierarchy Person -> Software System -> Container -> Component is what each view's permitted kinds are drawn from.</summary>
public enum C4ElementKind
{
    Person,
    SoftwareSystem,
    Container,
    Component,
    DeploymentNode,
    InfrastructureNode,
    ContainerInstance,
    SoftwareSystemInstance,
}

/// <summary>
/// One element of a C4 model, as the document declares it. <see cref="Line"/> is what makes an
/// edit surgical: a rename replaces that one line and leaves the document otherwise untouched
/// (c4-diagrams Requirement 3.2).
/// </summary>
/// <param name="Id">The DSL identifier, or a generated one when the declaration named none.</param>
/// <param name="Kind">Which C4 abstraction this is.</param>
/// <param name="Name">Its name, as shown on the diagram.</param>
/// <param name="Description">Its short description - C4 requires one on every element.</param>
/// <param name="Technology">Its technology; C4 requires one on every container and component.</param>
/// <param name="Tags">The tags it declares, which is how styles and "external" are applied.</param>
/// <param name="ParentId">The element it sits inside, or null at the top of the model.</param>
/// <param name="Line">The 1-based line its declaration begins on.</param>
/// <param name="ReferencedId">For an instance, the container or system it is an instance of.</param>
public sealed record C4Element(
    string Id,
    C4ElementKind Kind,
    string Name,
    string Description,
    string Technology,
    IReadOnlyList<string> Tags,
    string? ParentId,
    uint Line,
    string? ReferencedId = null)
{
    /// <summary>
    /// Whether this element is outside the scope of what the model describes. C4 draws such
    /// elements muted so scope is readable at a glance; the DSL expresses it as a tag.
    /// </summary>
    public bool IsExternal =>
        Tags.Any(tag => tag.Equals("External", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("Existing System", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// One relationship, which C4 requires to be unidirectional and labelled. It is a first-class
/// element on the wire rather than derived from a parent id, because unlike a mindmap's edges
/// it carries its own description, technology and actions (c4-diagrams design, "Deviations").
/// </summary>
public sealed record C4Relationship(
    string SourceId,
    string DestinationId,
    string Description,
    string Technology,
    uint Line)
{
    /// <summary>A stable id for the wire: relationships have no DSL identifier of their own.</summary>
    public string Id => $"{SourceId}->{DestinationId}@{Line}";
}
