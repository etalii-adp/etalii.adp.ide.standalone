namespace EtAlii.Adp.Diagram.C4;

/// <summary>
/// The C4 rules, as a pure function from a parsed workspace to the problems in it. No file, no
/// canvas, no connection - so every rule is testable from a plain string of DSL, which is what
/// the spec's non-functional requirements ask for (c4-diagrams Requirement 10).
/// </summary>
/// <remarks>
/// <para>
/// Everything here is a <b>warning</b>, and deliberately so. A model mid-edit is routinely
/// incomplete - a container gets its technology a moment after it gets its name - and a tool
/// that refuses to save an unfinished diagram is worse than one that says what is unfinished
/// (Requirement 10.7). The impossibilities, like a component that is not inside a container,
/// belong to the commands that would create them, not here.
/// </para>
/// <para>
/// No rule is expressed in terms of colour. C4 is notation independent and says so explicitly;
/// the palette is a default ADP applies, not something a document can be wrong about
/// (Requirement 4.8).
/// </para>
/// </remarks>
public static class C4RuleSet
{

    /// <summary>What each kind must sit inside, or null when it sits at the top of the model.</summary>
    /// <remarks>
    /// C4's hierarchy is Person -> Software System -> Container -> Component, and the DSL lets a
    /// document write an element in the wrong place - a component straight inside the model, say.
    /// A containment <em>cycle</em> is not checked because it cannot be written: the DSL nests
    /// lexically rather than by reference, so an element cannot contain its own ancestor
    /// (c4-diagrams Requirement 10.6).
    /// </remarks>
    private static C4ElementKind? RequiredParentOf(C4ElementKind kind) => kind switch
    {
        C4ElementKind.Container => C4ElementKind.SoftwareSystem,
        C4ElementKind.Component => C4ElementKind.Container,
        _ => null,
    };

    /// <summary>Which element kinds each view kind may show (Requirements 5.2, 6.1-6.2, 7.1, 7.7, 9.2-9.4, 9.6).</summary>
    public static IReadOnlyList<C4ElementKind> PermittedKinds(C4ViewKind viewKind) => viewKind switch
    {
        // "Everybody, technical and non-technical": people and software systems only.
        C4ViewKind.SystemContext => [C4ElementKind.Person, C4ElementKind.SoftwareSystem],
        // A landscape is a context diagram without a focus, so it shows the same kinds.
        C4ViewKind.SystemLandscape => [C4ElementKind.Person, C4ElementKind.SoftwareSystem],
        // The containers of one system, plus the people and systems directly connected to them.
        C4ViewKind.Container => [C4ElementKind.Person, C4ElementKind.SoftwareSystem, C4ElementKind.Container],
        // The components of one container, plus its sibling containers and outside connections.
        C4ViewKind.Component =>
            [C4ElementKind.Person, C4ElementKind.SoftwareSystem, C4ElementKind.Container, C4ElementKind.Component],
        // Any one level at runtime - which level is checked separately by the mixing rule.
        C4ViewKind.Dynamic =>
            [C4ElementKind.Person, C4ElementKind.SoftwareSystem, C4ElementKind.Container, C4ElementKind.Component],
        C4ViewKind.Deployment =>
            [C4ElementKind.DeploymentNode, C4ElementKind.InfrastructureNode, C4ElementKind.ContainerInstance, C4ElementKind.SoftwareSystemInstance],
        _ => [],
    };

    /// <summary>Every problem in <paramref name="workspace"/>, across the whole model and all its views.</summary>
    public static IReadOnlyList<DiagramProblem> Validate(C4Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var problems = new List<DiagramProblem>();

        // ADP reads only the primary document, so an !include leaves the model incomplete in a
        // way nothing else on screen would explain (design "Prerequisites and blockers" 4).
        foreach (var include in workspace.Includes)
        {
            problems.Add(new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"This model includes '{include}', which ADP does not read. Anything declared there is missing from this diagram, and ADP will not edit it.",
                C4Rules.IncludeNotFollowed));
        }

        problems.AddRange(ValidateElements(workspace));
        problems.AddRange(ValidateRelationships(workspace));
        foreach (var view in workspace.Views)
        {
            problems.AddRange(ValidateView(workspace, view));
        }

        problems.AddRange(ValidateCoverage(workspace));

        return problems;
    }

    /// <summary>
    /// Elements the model declares but no view draws, mirroring <c>model.element.noview</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Suppressed entirely while the model declares no views, for the same reason
    /// <c>c4.disconnected-element</c> is: everything is undrawn in a file that has no views yet,
    /// and a tool that greets a new file with warnings teaches people to ignore warnings.
    /// </para>
    /// <para>
    /// The design asks for a second suppression - not firing on an element added in the last
    /// edit - and it is deliberately not implemented. Validation is a pure function of the
    /// workspace with no edit history to read, and the substitute the task offered, treating
    /// the last-declared element as the just-added one, is not the same thing: an element
    /// appended to the model months ago is last-declared forever, and one inserted above it is
    /// never last-declared however recently it was typed. That would make the rule silent about
    /// one arbitrary element and no quieter about new ones, which is worse than not having the
    /// suppression at all.
    /// </para>
    /// </remarks>
    private static IEnumerable<DiagramProblem> ValidateCoverage(C4Workspace workspace)
    {
        if (workspace.Views.Count == 0)
        {
            yield break;
        }

        // MembersOf rather than a second reading of the include rules. The two disagreeing
        // would be worse than this rule not existing: it would report an element the view
        // panel is visibly drawing.
        var drawn = workspace.Views
            .SelectMany(view => MembersOf(workspace, view))
            .Select(element => element.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var element in workspace.Elements)
        {
            // An instance has no existence apart from the node it sits in and the container it
            // instantiates, both of which are drawn or not on their own account.
            if (element.Kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
            {
                continue;
            }

            if (!drawn.Contains(element.Id))
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' is on no view. An element the model declares but nothing draws is invisible to every reader of the diagrams.",
                    C4Rules.ElementNotOnAnyView,
                    new DiagramProblemElementLocation(element.Id));
            }
        }
    }
    /// <summary>
    /// Every element some relationship reaches, counting the ones C4 implies.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both ends of a relationship, because being pointed at is being connected: a database
    /// nothing calls out to is still part of the model. And the ancestors of both ends, because
    /// C4 draws the same conversation at several levels - a call from a container to a
    /// component of another container is those two containers relating, and their systems too.
    /// </para>
    /// <para>
    /// The subtlety, and the reason this is a symmetric difference rather than a union: an
    /// element is only reached by a relationship that *crosses* it. A call between two
    /// containers of one software system says nothing about that system's place in the
    /// landscape, so it must not mark the system connected - and an ancestor shared by both
    /// ends is exactly an ancestor the relationship stays inside.
    /// </para>
    /// <para>
    /// This is Structurizr's behaviour rather than a guess at it. An earlier version marked
    /// every ancestor connected, and the reconciliation against Structurizr's recorded verdicts
    /// caught it: Structurizr reports the Spring PetClinic software system as disconnected in
    /// its own AWS example, where the containers inside it talk to each other and nothing
    /// outside talks to it. It does not report the API Application container in C4's worked
    /// example, whose components are called from a sibling container. Nothing but comparing the
    /// two tools on real files would have told these apart.
    /// </para>
    /// </remarks>
    private static HashSet<string> ConnectedElements(C4Workspace workspace)
    {
        var connected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var relationship in workspace.Relationships)
        {
            var source = AncestorsAndSelf(workspace, relationship.SourceId);
            var destination = AncestorsAndSelf(workspace, relationship.DestinationId);

            connected.UnionWith(source.Where(id => !destination.Contains(id)));
            connected.UnionWith(destination.Where(id => !source.Contains(id)));
        }

        return connected;
    }

    /// <summary>An element's id and every id above it, or nothing when the id names no element.</summary>
    private static HashSet<string> AncestorsAndSelf(C4Workspace workspace, string id)
    {
        var chain = new HashSet<string>(StringComparer.Ordinal);
        for (var element = workspace.Find(id); element is not null && chain.Add(element.Id);)
        {
            element = element.ParentId is { } parentId ? workspace.Find(parentId) : null;
        }

        return chain;
    }
    private static IEnumerable<DiagramProblem> ValidateElements(C4Workspace workspace)
    {
        var connected = ConnectedElements(workspace);

        // Suppressed entirely while the model declares no views. A model is built up a line at
        // a time, and every element is disconnected for the minute between being declared and
        // being wired up. A tool that greets a new file with warnings teaches people to ignore
        // warnings, so this one waits until there is a view to be inconsistent with.
        var modelIsUnderway = workspace.Views.Count > 0;

        foreach (var element in workspace.Elements)
        {
            // An instance has no name or description of its own: it is its container, deployed.
            if (element.Kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
            {
                continue;
            }

            // Every element that is not an instance of another. This rule was once narrowed to
            // the four static kinds, on the reasoning that a deployment node is named by what it
            // is - "Apache Tomcat", "bigbank-web***" - and that C4's own worked example leaves
            // those undescribed. Structurizr's own inspector reports
            // `model.deploymentnode.description` on exactly those nodes, so the example is
            // authoritative about syntax and not about quality. The narrowing was the error.
            if (element.Description.Length == 0)
            {
                // A separate id per kind, because Structurizr names one per kind and the mirror
                // table compares by id. Same question, same wording, different rule.
                var ruleId = element.Kind switch
                {
                    C4ElementKind.DeploymentNode => C4Rules.MissingDeploymentDescription,
                    C4ElementKind.InfrastructureNode => C4Rules.MissingInfrastructureDescription,
                    _ => C4Rules.MissingDescription,
                };

                yield return new DiagramProblem(
                    // A Warning, where Structurizr prints ERROR. These are recommendations
                    // rather than syntax faults, and the divergence is a recorded decision
                    // (quality-gates Requirement 1.9).
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' has no description. C4 asks for a short description on every element, so a reader can tell at a glance what it is responsible for.",
                    ruleId,
                    new DiagramProblemElementLocation(element.Id));
            }

            // "Every container and component should have a technology explicitly specified."
            if (element.Kind is C4ElementKind.Container or C4ElementKind.Component && element.Technology.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' has no technology. C4 asks for one explicitly on every {element.Kind.ToString().ToLowerInvariant()}.",
                    C4Rules.MissingTechnology,
                    new DiagramProblemElementLocation(element.Id));
            }

            // A deployment node's technology is the thing it actually runs - "Ubuntu 24.04",
            // "Docker", "Apache Tomcat 10" - and an infrastructure node's is what provides it,
            // like "Elastic Load Balancer". Structurizr names one rule per kind, and so does
            // this, so the mirror table can compare them one for one rather than by set.
            var nodeTechnologyRule = element.Kind switch
            {
                C4ElementKind.DeploymentNode => C4Rules.MissingDeploymentTechnology,
                C4ElementKind.InfrastructureNode => C4Rules.MissingInfrastructureTechnology,
                _ => null,
            };

            if (nodeTechnologyRule is not null && element.Technology.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' names no technology. A deployment diagram is read to find out what actually runs where, so say what this node is.",
                    nodeTechnologyRule,
                    // By element id rather than by line, so selecting the problem in the errors
                    // panel selects the node on the canvas.
                    new DiagramProblemElementLocation(element.Id));
            }

            // The static abstractions only. A deployment node is joined to the model by being
            // nested inside another, not by a relationship, so asking one to participate in a
            // relationship asks the wrong question of it - the AWS example would report every
            // node it has. This is not a narrowing to keep a fixture quiet: the reconciliation
            // against Structurizr's own verdicts will say whether it agrees.
            var canBeDisconnected = element.Kind is
                C4ElementKind.Person or C4ElementKind.SoftwareSystem or C4ElementKind.Container or C4ElementKind.Component;

            if (modelIsUnderway && canBeDisconnected && !connected.Contains(element.Id))
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' is in no relationship at all. An element nothing reaches and that reaches nothing is either unfinished or left over.",
                    C4Rules.DisconnectedElement,
                    new DiagramProblemElementLocation(element.Id));
            }

            // C4's hierarchy is what gives each level its meaning: a component is a part of a
            // container, and one written outside a container is not a C4 component at all.
            if (RequiredParentOf(element.Kind) is { } required)
            {
                var parent = element.ParentId is { } parentId ? workspace.Find(parentId) : null;
                if (parent is null || parent.Kind != required)
                {
                    yield return new DiagramProblem(
                        DiagramProblemSeverity.Warning,
                        parent is null
                            ? $"'{Label(element)}' is a {Spell(element.Kind)} declared outside any {Spell(required)}. In C4 a {Spell(element.Kind)} is part of a {Spell(required)}."
                            : $"'{Label(element)}' is a {Spell(element.Kind)} inside a {Spell(parent.Kind)}. In C4 a {Spell(element.Kind)} is part of a {Spell(required)}.",
                        C4Rules.MisplacedElement,
                        new DiagramProblemElementLocation(element.Id));
                }
            }
        }
    }

    private static IEnumerable<DiagramProblem> ValidateRelationships(C4Workspace workspace)
    {
        foreach (var relationship in workspace.Relationships)
        {
            var source = workspace.Find(relationship.SourceId);
            var destination = workspace.Find(relationship.DestinationId);

            if (source is null || destination is null)
            {
                var missing = source is null ? relationship.SourceId : relationship.DestinationId;
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The relationship on line {relationship.Line} names '{missing}', which the model does not declare.",
                    C4Rules.DanglingRelationship,
                    new DiagramProblemLineLocation(relationship.Line));
                continue;
            }

            // "Relationships should be labelled, the label being consistent with the direction."
            if (relationship.Description.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The relationship from '{Label(source)}' to '{Label(destination)}' has no label. C4 asks what the relationship is for, in the direction of the arrow.",
                    C4Rules.UnlabelledRelationship,
                    new DiagramProblemLineLocation(relationship.Line));
            }

            // Every relationship, matching `model.relationship.technology`. This was once
            // narrowed to container-to-container, reasoning that two components of one
            // container call each other in process and have no protocol to name, and that
            // C4's worked example leaves those undecorated. Structurizr inspects every
            // relationship and reports 26 findings on that same example, so the narrowing
            // rested on a fixture rather than on the notation.
            //
            // An in-process call does have something worth naming - the example's own
            // component relationships say "JDBC", and a plain method call is worth writing
            // down as one when a reader is trying to tell it from a network hop.
            if (relationship.Technology.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The relationship from '{Label(source)}' to '{Label(destination)}' names no technology. How two elements communicate - a protocol, a library, a call - is what tells a reader where a boundary really is.",
                    C4Rules.MissingProtocol,
                    new DiagramProblemLineLocation(relationship.Line));
            }
        }
    }

    private static IEnumerable<DiagramProblem> ValidateView(C4Workspace workspace, C4View view)
    {
        if (view.ScopeId is { } scopeId && workspace.Find(scopeId) is null)
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The '{view.Key}' view is scoped to '{scopeId}', which the model does not declare.",
                C4Rules.UnknownViewScope,
                new DiagramProblemLineLocation(view.Line));
        }

        var members = MembersOf(workspace, view);
        var permitted = PermittedKinds(view.Kind);
        foreach (var element in members.Where(element => !permitted.Contains(element.Kind)))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The '{view.Key}' view shows '{Label(element)}', which is a {Spell(element.Kind)}. A {Spell(view.Kind)} view shows {string.Join(", ", permitted.Select(Spell))}.",
                C4Rules.KindNotPermitted,
                new DiagramProblemElementLocation(element.Id));
        }

        // There is deliberately no "mixed abstraction levels" rule for dynamic views.
        // Requirement 7.7 asserted one, reading C4's "software systems, containers or
        // components" strictly - but C4's own worked example mixes them: its sign-in view is
        // scoped to a container and shows that container's components alongside the
        // single-page application and the database, which are containers. A component view
        // shows sibling containers for the same reason.
        //
        // This used to say the canonical example is the better authority, so the rule went
        // rather than the example being called wrong. That argument is not one to reuse - it
        // narrowed two other rules that were right, because a published example demonstrates
        // what is permitted and never what is sufficient. What this removal actually stands on
        // is that Structurizr's inspector has no such rule at all, which is a fact about the
        // notation rather than about one file.

        if (members.Count == 0 && view.Interactions.Count == 0)
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The '{view.Key}' view is empty.",
                C4Rules.EmptyView,
                new DiagramProblemLineLocation(view.Line));
        }
    }

    /// <summary>
    /// The elements a view actually shows. <c>include *</c> means everything the view's kind
    /// permits within its scope, which is why the permitted-kind rule can only fire for
    /// elements a view names explicitly - a wildcard cannot be wrong about a kind.
    /// </summary>
    public static IReadOnlyList<C4Element> MembersOf(C4Workspace workspace, C4View view)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(view);

        var permitted = PermittedKinds(view.Kind);
        var named = view.Includes
            .Select(workspace.Find)
            .Where(element => element is not null)
            .Select(element => element!)
            .ToList();

        if (view.IncludesEverything)
        {
            named.AddRange(workspace.Elements.Where(element => permitted.Contains(element.Kind)));
        }

        return named
            .Where(element => !view.Excludes.Contains(element.Id, StringComparer.OrdinalIgnoreCase))
            .Where(element => !IsTheBoundaryItself(view, element))
            .DistinctBy(element => element.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>
    /// Whether this element is the thing the view is *inside of* rather than something on it. On
    /// a container diagram the software system in scope is the boundary drawn around the
    /// containers, and on a component diagram the container is; drawing it as a box as well
    /// would put the system inside its own boundary (found by the manual pass).
    /// </summary>
    private static bool IsTheBoundaryItself(C4View view, C4Element element) =>
        view.Kind is C4ViewKind.Container or C4ViewKind.Component
        && view.ScopeId is { } scopeId
        && string.Equals(element.Id, scopeId, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a kind is something that runs and therefore talks over a protocol.</summary>
    // private static bool IsDeployable(C4ElementKind kind) => kind is C4ElementKind.Container or C4ElementKind.Component;

    private static string Label(C4Element element) => element.Name.Length > 0 ? element.Name : element.Id;

    private static string Spell(C4ElementKind kind) => kind switch
    {
        C4ElementKind.SoftwareSystem => "software system",
        C4ElementKind.DeploymentNode => "deployment node",
        C4ElementKind.InfrastructureNode => "infrastructure node",
        C4ElementKind.ContainerInstance => "container instance",
        C4ElementKind.SoftwareSystemInstance => "software system instance",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string Spell(C4ViewKind kind) => kind switch
    {
        C4ViewKind.SystemLandscape => "system landscape",
        C4ViewKind.SystemContext => "system context",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
