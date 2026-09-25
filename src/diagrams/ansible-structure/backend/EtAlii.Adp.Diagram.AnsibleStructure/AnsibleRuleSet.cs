namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// What is wrong with an Ansible project, in an Ansible user's terms (Requirement 9).
/// </summary>
/// <remarks>
/// <para>
/// A pure function from the model to problems. It reads no file: the reader already did that
/// once, tolerantly, keeping the line marks and the parse failures. That is what makes every
/// rule testable from a temp tree with no gRPC, no store and no watcher - and what stops the
/// diagram and the problems panel ever disagreeing about what is in the folder, since they are
/// looking at the same object.
/// </para>
/// <para>
/// <b>Silence is a valid answer, and the harder half of the job.</b> A team that keeps its
/// playbooks in <c>plays/</c> has an unrecognised layout, not a broken project: they get an
/// undrawn folder and nothing in the panel. Every rule below is written to fire on a mistake
/// somebody made, never on a convention nobody agreed to (Requirement 9.4).
/// </para>
/// </remarks>
public static class AnsibleRuleSet
{
    /// <summary>
    /// Host patterns that name something real without any inventory having to define it.
    /// Reporting these would fire on ordinary, correct playbooks - which is how a panel teaches
    /// people to stop reading it.
    /// </summary>
    private static readonly string[] ImplicitHosts = ["all", "*", "localhost", "127.0.0.1", "::1"];

    /// <summary>Everything wrong with <paramref name="project"/>, or none.</summary>
    public static IReadOnlyList<DiagramProblem> Judge(AnsibleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var graph = AnsibleGraph.Derive(project);
        var problems = new List<DiagramProblem>();

        problems.AddRange(UnreadableYaml(project));
        problems.AddRange(MissingTargets(graph));
        problems.AddRange(UnmatchedHosts(project));
        problems.AddRange(EmptyRoles(project));

        return problems;
    }

    /// <summary>
    /// A file that would not parse, in the parser's own words and at its own line. Reported
    /// against the file itself rather than the diagram, so the panel opens the thing to fix.
    /// </summary>
    private static IEnumerable<DiagramProblem> UnreadableYaml(AnsibleProject project) =>
        project.Failures.Select(failure => new DiagramProblem(
            DiagramProblemSeverity.Error,
            $"'{failure.RelativePath}' is not readable YAML: {failure.Message}",
            AnsibleRules.UnreadableYaml,
            new DiagramProblemFileLocation(failure.RelativePath, failure.Line)));

    /// <summary>
    /// An edge that names something the folder does not hold. Split into two rules because a
    /// reader fixes them differently: a missing role means writing a role or correcting a name,
    /// a dangling import means a file that moved or was never added.
    /// </summary>
    /// <remarks>
    /// <see cref="AnsibleTargetResolution.Unresolvable"/> is deliberately not reported. Its
    /// target is a Jinja expression, so what it names is unknowable without running Ansible -
    /// and a rule that treated unknown as wrong would fire on every parameterised role in every
    /// real repository (Requirement 3.4).
    /// </remarks>
    private static IEnumerable<DiagramProblem> MissingTargets(AnsibleGraph graph) =>
        graph.Edges
            .Where(edge => edge.Resolution == AnsibleTargetResolution.Missing)
            .Select(edge => edge.Kind switch
            {
                AnsibleEdgeKind.UsesRole => new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"The role '{edge.Directive.Target}' has no folder under roles/.",
                    AnsibleRules.RoleMissing,
                    Where(edge)),

                AnsibleEdgeKind.DependsOn => new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"The role '{edge.Directive.Target}' is declared as a dependency but has no folder under roles/.",
                    AnsibleRules.RoleMissing,
                    Where(edge)),

                _ => new DiagramProblem(
                    DiagramProblemSeverity.Error,
                    $"'{edge.Directive.Target}' is {edge.Directive.Kind switch
                    {
                        AnsibleDirectiveKind.ImportPlaybook => "imported",
                        AnsibleDirectiveKind.ImportTasks => "imported",
                        _ => "included",
                    }} here, but there is no such file.",
                    AnsibleRules.DanglingImport,
                    Where(edge)),
            });

    /// <summary>
    /// A play aimed at a group nothing defines - the mistake that deploys nothing and says so
    /// only at run time.
    /// </summary>
    /// <remarks>
    /// Two guards keep this from becoming noise, and both are the difference between a rule and
    /// a nuisance. It is silent when the project has <b>no inventory at all</b>: "no inventory
    /// defines it" presupposes there are inventories to do the defining, and a team that keeps
    /// theirs outside the folder would otherwise be warned about every play they have. And it
    /// ignores the patterns Ansible resolves without an inventory entry - <c>all</c>,
    /// <c>localhost</c> and their kin.
    /// </remarks>
    private static IEnumerable<DiagramProblem> UnmatchedHosts(AnsibleProject project)
    {
        if (project.Inventories.Count == 0)
        {
            yield break;
        }

        foreach (var playbook in project.Playbooks)
        {
            foreach (var play in playbook.Plays)
            {
                if (play.Hosts.Length == 0 || IsImplicit(play.Hosts))
                {
                    continue;
                }

                if (project.Inventories.Any(inventory => AnsibleGraph.Matches(play.Hosts, inventory)))
                {
                    continue;
                }

                var named = play.Name.Length > 0 ? $"The play '{play.Name}'" : "A play";
                yield return new DiagramProblem(
                    DiagramProblemSeverity.Warning,
                    $"{named} targets '{play.Hosts}', which no inventory defines.",
                    AnsibleRules.UnmatchedHosts,
                    new DiagramProblemFileLocation(playbook.RelativePath, play.Line));
            }
        }
    }

    /// <summary>
    /// A folder under <c>roles/</c> with no canonical content: Ansible would find nothing to run
    /// in it, so a playbook naming it does less than its author thinks.
    /// </summary>
    private static IEnumerable<DiagramProblem> EmptyRoles(AnsibleProject project) =>
        project.Roles
            .Where(role => role.Contents.IsHollow)
            .Select(role => new DiagramProblem(
                DiagramProblemSeverity.Warning,
                $"The role '{role.Name}' has none of the folders Ansible looks in, so it does nothing.",
                AnsibleRules.EmptyRole,
                new DiagramProblemFileLocation(role.RelativePath)));

    /// <summary>
    /// The file and line that declared an edge - the place a reader has to open, which is never
    /// the <c>.adp</c> and often not the file they were looking at.
    /// </summary>
    private static DiagramProblemFileLocation Where(AnsibleEdge edge) =>
        new(edge.Directive.DeclaredIn, edge.Directive.Line);

    private static bool IsImplicit(string hosts) =>
        hosts.Split([':', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(token => ImplicitHosts.Contains(token, StringComparer.OrdinalIgnoreCase));
}
