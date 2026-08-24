using EtAlii.Adp.Diagram;

namespace EtAlii.Adp.C4;

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
    /// <summary>Every rule's stable id, prefixed with the module's short name as core expects.</summary>
    public static class Rules
    {
        public const string MissingDescription = "c4.missing-description";
        public const string MissingTechnology = "c4.missing-technology";
        public const string UnlabelledRelationship = "c4.unlabelled-relationship";
        public const string MissingProtocol = "c4.missing-protocol";
        public const string DanglingRelationship = "c4.dangling-relationship";
        public const string KindNotPermitted = "c4.kind-not-permitted-on-view";
        public const string MixedAbstractionLevels = "c4.mixed-abstraction-levels";
        public const string EmptyView = "c4.empty-view";
        public const string UnknownViewScope = "c4.unknown-view-scope";
    }

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
        problems.AddRange(ValidateElements(workspace));
        problems.AddRange(ValidateRelationships(workspace));
        foreach (var view in workspace.Views)
        {
            problems.AddRange(ValidateView(workspace, view));
        }

        return problems;
    }

    private static IEnumerable<DiagramProblem> ValidateElements(C4Workspace workspace)
    {
        foreach (var element in workspace.Elements)
        {
            // An instance has no name or description of its own: it is its container, deployed.
            if (element.Kind is C4ElementKind.ContainerInstance or C4ElementKind.SoftwareSystemInstance)
            {
                continue;
            }

            if (element.Description.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' has no description. C4 asks for a short description on every element, so a reader can tell at a glance what it is responsible for.",
                    Rules.MissingDescription,
                    new DiagramProblemLocation.ElementId(element.Id));
            }

            // "Every container and component should have a technology explicitly specified."
            if (element.Kind is C4ElementKind.Container or C4ElementKind.Component && element.Technology.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"'{Label(element)}' has no technology. C4 asks for one explicitly on every {element.Kind.ToString().ToLowerInvariant()}.",
                    Rules.MissingTechnology,
                    new DiagramProblemLocation.ElementId(element.Id));
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
                    Rules.DanglingRelationship,
                    new DiagramProblemLocation.Line(relationship.Line));
                continue;
            }

            // "Relationships should be labelled, the label being consistent with the direction."
            if (relationship.Description.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The relationship from '{Label(source)}' to '{Label(destination)}' has no label. C4 asks what the relationship is for, in the direction of the arrow.",
                    Rules.UnlabelledRelationship,
                    new DiagramProblemLocation.Line(relationship.Line));
            }

            // A relationship between containers is where the protocol matters, because that is
            // what a Container diagram exists to communicate (Requirement 6.6).
            var crossesABoundary = IsDeployable(source.Kind) && IsDeployable(destination.Kind);
            if (crossesABoundary && relationship.Technology.Length == 0)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The relationship from '{Label(source)}' to '{Label(destination)}' names no technology. How two containers communicate is what a Container diagram is for.",
                    Rules.MissingProtocol,
                    new DiagramProblemLocation.Line(relationship.Line));
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
                Rules.UnknownViewScope,
                new DiagramProblemLocation.Line(view.Line));
        }

        var members = MembersOf(workspace, view);
        var permitted = PermittedKinds(view.Kind);
        foreach (var element in members.Where(element => !permitted.Contains(element.Kind)))
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The '{view.Key}' view shows '{Label(element)}', which is a {Spell(element.Kind)}. A {Spell(view.Kind)} view shows {string.Join(", ", permitted.Select(Spell))}.",
                Rules.KindNotPermitted,
                new DiagramProblemLocation.ElementId(element.Id));
        }

        // A dynamic view tells one story at one level: mixing systems with the components
        // inside them makes the story unreadable (Requirement 7.7).
        if (view.Kind == C4ViewKind.Dynamic)
        {
            var levels = view.Interactions
                .SelectMany(interaction => new[] { interaction.SourceId, interaction.DestinationId })
                .Select(workspace.Find)
                .Where(element => element is not null)
                .Select(element => element!.Kind)
                .Where(kind => kind != C4ElementKind.Person)
                .Distinct()
                .ToArray();

            if (levels.Length > 1)
            {
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"The '{view.Key}' view mixes {string.Join(" and ", levels.Select(Spell))}. A dynamic view stays at one level of abstraction.",
                    Rules.MixedAbstractionLevels,
                    new DiagramProblemLocation.Line(view.Line));
            }
        }

        if (members.Count == 0 && view.Interactions.Count == 0)
        {
            yield return new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The '{view.Key}' view is empty.",
                Rules.EmptyView,
                new DiagramProblemLocation.Line(view.Line));
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
    private static bool IsDeployable(C4ElementKind kind) =>
        kind is C4ElementKind.Container or C4ElementKind.Component;

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
