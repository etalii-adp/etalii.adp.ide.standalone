namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// Which of ADP's C4 rules mirror a rule in Structurizr's own <c>inspect</c>, which are ADP's
/// alone, and which of Structurizr's are deliberately not implemented.
/// </summary>
/// <remarks>
/// <para>
/// This table exists because ADP takes a position on what a good C4 model is, and Structurizr
/// takes one too. Where both have an opinion they must agree, or a model ADP calls clean turns
/// up a page of findings the first time a colleague runs Structurizr over it. The reconciliation
/// test reads this table to know which rules to compare; everything outside it is a decision
/// rather than a disagreement.
/// </para>
/// <para>
/// It is also the correction of a mistake. Two of ADP's rules were once narrowed because they
/// fired on C4's own worked example, on the reasoning that the example is what C4 holds up as
/// done properly. Structurizr's inspector reports 26 findings on that same example, including
/// exactly the ones that prompted the narrowing - so the example is authoritative about syntax
/// and not about quality. A published example demonstrates what is *permitted*, never what is
/// *sufficient*.
/// </para>
/// </remarks>
public static class C4StructurizrMirror
{
    /// <summary>
    /// ADP rule id to the Structurizr rule it mirrors. A rule listed here is compared against
    /// Structurizr's verdict on every fixture; a difference is a defect in one of them.
    /// </summary>
    /// <remarks>
    /// <c>c4.missing-description</c> and <c>c4.missing-technology</c> each answer for several
    /// Structurizr rules, because Structurizr names one per element kind and ADP names one per
    /// question. The comparison is by set, so a one-to-many mapping costs nothing.
    /// </remarks>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> MirroredRules { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [C4Rules.MissingDescription] =
            [
                "model.person.description",
                "model.softwaresystem.description",
                "model.container.description",
                "model.component.description",
            ],
            [C4Rules.MissingTechnology] = ["model.container.technology"],
            [C4Rules.MissingProtocol] = ["model.relationship.technology"],
            [C4Rules.MissingDeploymentDescription] = ["model.deploymentnode.description"],
            [C4Rules.MissingDeploymentTechnology] = ["model.deploymentnode.technology"],
            [C4Rules.MissingInfrastructureDescription] = ["model.infrastructurenode.description"],
            [C4Rules.MissingInfrastructureTechnology] = ["model.infrastructurenode.technology"],
            [C4Rules.DisconnectedElement] = ["model.element.disconnected"],
            [C4Rules.ElementNotOnAnyView] = ["model.element.noview"],
        };

    /// <summary>
    /// ADP's own rules, with no Structurizr counterpart, and kept.
    /// </summary>
    /// <remarks>
    /// These guard ADP's editing surface rather than the model's quality: what a view may
    /// contain, whether an identifier resolves, whether an <c>!include</c> was followed. A model
    /// can be perfect by Structurizr's lights and still trip one of them, because they answer a
    /// question Structurizr never asks - "can this be edited here?" rather than "is this good?".
    /// </remarks>
    public static IReadOnlyDictionary<string, string> AdpOnlyRules { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [C4Rules.UnlabelledRelationship] =
                "C4 requires every relationship to be labelled. Structurizr inspects for a technology but not for a description.",
            [C4Rules.DanglingRelationship] =
                "An identifier that resolves to nothing. Structurizr's parser refuses the document outright, so its inspector never sees one.",
            [C4Rules.KindNotPermitted] =
                "What a view of each kind may draw. An editing-surface rule: it stops a user putting a component on a context diagram.",
            [C4Rules.EmptyView] =
                "A view that would draw nothing. Structurizr has the inverse - an element on no view - which ADP now mirrors separately.",
            [C4Rules.UnknownViewScope] =
                "A view scoped to something the model does not declare. Refused by Structurizr's parser rather than reported by its inspector.",
            [C4Rules.MisplacedElement] =
                "A component outside a container, or a container outside a software system. Refused by Structurizr's parser rather than inspected.",
            [C4Rules.IncludeNotFollowed] =
                "ADP does not follow `!include`, and says so rather than silently reading half a model. Structurizr follows it, so it has nothing to report.",
        };

    /// <summary>
    /// Structurizr rules ADP does not implement, each with what would have to exist first.
    /// </summary>
    /// <remarks>
    /// Listed rather than omitted so that "not mirrored" is a decision on the record. The
    /// reconciliation test excludes these from the comparison; without the list it could not
    /// tell a deliberate gap from an oversight.
    /// </remarks>
    public static IReadOnlyDictionary<string, string> NotImplementedRules { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workspace.scope"] =
                "A Structurizr workspace-metadata concept. ADP writes one workspace per document and has nothing to set a scope from, so mirroring it would produce a warning a user could not act on.",
            ["model.softwaresystem.documentation"] =
                "Depends on `!docs`, which ADP round-trips but does not read. A documentation model would have to exist first.",
            ["model.softwaresystem.decisions"] =
                "Depends on `!adrs`, likewise. ADP carries the directive and the folder untouched and has no notion of a decision record.",
        };

    /// <summary>The Structurizr rule ids that take part in the comparison, flattened.</summary>
    public static IReadOnlySet<string> MirroredStructurizrRules { get; } =
        MirroredRules.Values.SelectMany(rules => rules).ToHashSet(StringComparer.Ordinal);
}
