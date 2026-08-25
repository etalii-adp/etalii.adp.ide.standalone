namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The kind of view one C4 diagram shows. Six of the seven C4 diagram types bind exactly one
/// of these, which is the whole difference between them: the modules are otherwise identical
/// registrations over one shared engine (c4-diagrams design, "Overview").
/// </summary>
/// <remarks>
/// There is deliberately no <c>Code</c> member. The Structurizr DSL declares no code view, and
/// C4 itself advises against hand-drawing that level - <c>c4/code</c> delegates its notation to
/// a class or ER diagram type instead (c4-diagrams Requirement 11).
/// </remarks>
public enum C4ViewKind
{
    /// <summary>People and software systems across an organisation, with no focal system (Requirement 9.6).</summary>
    SystemLandscape,

    /// <summary>One software system in its world of people and other systems (Requirement 5).</summary>
    SystemContext,

    /// <summary>The applications and data stores inside one software system (Requirement 6).</summary>
    Container,

    /// <summary>The components inside one container (Requirement 7.1-7.4).</summary>
    Component,

    /// <summary>One feature or use case, as numbered interactions (Requirement 7.5-7.8).</summary>
    Dynamic,

    /// <summary>Containers mapped onto infrastructure in one environment (Requirement 9.1-9.5).</summary>
    Deployment,
}
