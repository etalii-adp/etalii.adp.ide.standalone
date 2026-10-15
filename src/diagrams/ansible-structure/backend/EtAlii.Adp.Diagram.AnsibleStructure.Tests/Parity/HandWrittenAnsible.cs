using EtAlii.Adp.Context;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The property rows and the refusals as <c>AnsibleContextPropertyProvider</c> and <c>AnsibleSession</c> wrote
/// them by hand before they were derived from the DISL definition (ansible-structure-disl-fbl, task 5), and the
/// findings as <c>AnsibleRuleSet</c> judged them (task 6), kept verbatim as the oracle the derived ones are
/// compared with, folder for folder and element for element (Requirement 3.6).
/// </summary>
internal static class HandWrittenAnsible
{
    /// <summary>The sentence <c>SetAsync</c> refused every write with.</summary>
    public const string Refusal =
        "An Ansible structure diagram shows the folder as it is and changes nothing in it. " +
        "Edit the file this value comes from in a text editor.";

    /// <summary>The sentence <c>MoveElementAsync</c> refused every reparenting move with.</summary>
    public const string MoveRefusal =
        "An Ansible structure diagram is drawn from the folder's own files, so nothing on it can be moved from here. " +
        "Move a role by moving its folder.";

    /// <summary>The rows of <paramref name="elementId"/> in <paramref name="project"/>, as <c>DescribeAsync</c> answered them once it had the project: none for the empty id and for an id the graph does not hold.</summary>
    public static IReadOnlyList<ContextPropertyDefinition> Rows(AnsibleProject project, string elementId)
    {
        if (elementId.Length == 0)
        {
            return [];
        }

        var graph = AnsibleGraph.Derive(project);

        var node = graph.Node(elementId);
        if (node is not null)
        {
            return Describe(project, graph, node);
        }

        var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, elementId, StringComparison.Ordinal));
        return edge is null ? [] : Describe(edge);
    }

    // ---- nodes -------------------------------------------------------------------------------

    private static IReadOnlyList<ContextPropertyDefinition> Describe(AnsibleProject project, AnsibleGraph graph, AnsibleNode node) =>
        node.Kind switch
        {
            AnsibleNodeKind.Playbook or AnsibleNodeKind.Play => Playbook(project, graph, node),
            AnsibleNodeKind.Role => Role(project, graph, node),
            AnsibleNodeKind.Inventory => Inventory(project, node),
            AnsibleNodeKind.VariableFolder => VariableFolder(project, node),
            _ => TaskFile(node),
        };

    /// <summary>Requirement 10.3: Identity / Runs / Targets.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> Playbook(AnsibleProject project, AnsibleGraph graph, AnsibleNode node)
    {
        var playbook = project.Playbooks.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));

        var rows = new List<ContextPropertyDefinition>
        {
            Row("name", "Name", node.Name, node.RelativePath, "Identity"),
            Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
        };

        if (playbook is not null)
        {
            // Absent rather than empty when there is nothing to say (Requirement 10.7).
            if (playbook.Plays.Count > 0)
            {
                rows.Add(Row("plays", "Plays", Plays(playbook), node.RelativePath, "Runs"));
            }

            var roles = graph.Edges
                .Where(edge => edge.SourceId == node.Id && edge.Kind == AnsibleEdgeKind.UsesRole)
                .Select(edge => edge.Directive.Target)
                .ToArray();
            if (roles.Length > 0)
            {
                rows.Add(Row("roles", "Roles", string.Join(", ", roles), node.RelativePath, "Runs"));
            }

            var imports = playbook.Imports.Select(import => import.Target).ToArray();
            if (imports.Length > 0)
            {
                rows.Add(Row("imports", "Imports", string.Join(", ", imports), node.RelativePath, "Runs"));
            }
        }

        var hosts = HostsOf(playbook, node);
        if (hosts.Length > 0)
        {
            rows.Add(Row("hosts", "Hosts", hosts, node.RelativePath, "Targets"));
        }

        var inventories = graph.Edges
            .Where(edge => edge.SourceId == node.Id && edge.Kind == AnsibleEdgeKind.Targets)
            .Select(edge => graph.Node(edge.TargetId)?.Name ?? "")
            .Where(name => name.Length > 0)
            .ToArray();
        if (inventories.Length > 0)
        {
            rows.Add(Row("inventories", "Reaches", string.Join(", ", inventories), node.RelativePath, "Targets"));
        }

        return rows;
    }

    /// <summary>Requirement 10.4: Identity / Contents / Relationships.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> Role(AnsibleProject project, AnsibleGraph graph, AnsibleNode node)
    {
        var role = project.Role(node.Name);
        var rows = new List<ContextPropertyDefinition>
        {
            Row("name", "Name", node.Name, node.RelativePath, "Identity"),
            Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
        };

        if (role is not null)
        {
            foreach ((string label, int count, string id) in Contents(role.Contents))
            {
                // A subfolder that is not there is not contributed at all, so the grid shows
                // what a role has rather than a checklist of what it lacks.
                if (count > 0)
                {
                    rows.Add(Row(id, label, count.ToString(), node.RelativePath, "Contents"));
                }
            }

            if (role.Contents.HasMeta)
            {
                rows.Add(Row("meta", "Meta", "yes", $"{node.RelativePath}/meta/main.yml", "Contents"));
            }

            if (role.Contents.IsHollow)
            {
                rows.Add(Row("hollow", "Hollow", "yes - Ansible would find nothing to run here", node.RelativePath, "Contents"));
            }

            var dependencies = role.Dependencies.Select(dependency => dependency.Target).ToArray();
            if (dependencies.Length > 0)
            {
                rows.Add(Row(
                    "dependencies", "Depends on", string.Join(", ", dependencies),
                    $"{node.RelativePath}/meta/main.yml", "Relationships"));
            }
        }

        var usedBy = graph.Edges
            .Where(edge => edge.TargetId == node.Id && edge.Kind == AnsibleEdgeKind.UsesRole)
            .Select(edge => edge.Directive.DeclaredIn)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (usedBy.Length > 0)
        {
            rows.Add(Row("used-by", "Used by", string.Join(", ", usedBy), node.RelativePath, "Relationships"));
        }

        return rows;
    }

    /// <summary>Requirement 10.5: Identity / Groups / Variables.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> Inventory(AnsibleProject project, AnsibleNode node)
    {
        var inventory = project.Inventories.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));

        var rows = new List<ContextPropertyDefinition>
        {
            Row("name", "Environment", node.Name, node.RelativePath, "Identity"),
            Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
        };

        if (inventory is not null)
        {
            foreach (var group in inventory.Groups)
            {
                rows.Add(Row(
                    $"group.{group.Name}", group.Name,
                    group.HostCount == 1 ? "1 host" : $"{group.HostCount} hosts",
                    node.RelativePath, "Groups"));
            }

            foreach (var folder in inventory.VariableFolders)
            {
                rows.Add(Row(
                    $"vars.{folder.Name}", folder.Name,
                    folder.FileCount == 1 ? "1 file" : $"{folder.FileCount} files",
                    folder.RelativePath, "Variables"));
            }
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> VariableFolder(AnsibleProject project, AnsibleNode node)
    {
        var folder = project.Inventories
            .SelectMany(inventory => inventory.VariableFolders)
            .FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));

        var rows = new List<ContextPropertyDefinition>
        {
            Row("name", "Name", node.Name, node.RelativePath, "Identity"),
            Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
        };

        if (folder is not null)
        {
            rows.Add(Row("files", "Files", folder.FileCount.ToString(), node.RelativePath, "Variables"));
        }

        return rows;
    }

    private static IReadOnlyList<ContextPropertyDefinition> TaskFile(AnsibleNode node) =>
    [
        Row("name", "Name", node.Name, node.RelativePath, "Identity"),
        Row("path", "Path", node.RelativePath, node.RelativePath, "Identity"),
    ];

    /// <summary>Requirement 10.6: what declared this edge, and what it says as written.</summary>
    private static IReadOnlyList<ContextPropertyDefinition> Describe(AnsibleEdge edge)
    {
        var declaredIn = edge.Directive.DeclaredIn;
        var rows = new List<ContextPropertyDefinition>
        {
            Row("declared-in", "Declared in", $"{declaredIn}:{edge.Directive.Line}", declaredIn, "Declaration"),
            Row("directive", "Directive", DirectiveLabel(edge.Directive.Kind), declaredIn, "Declaration"),
            Row("target", "Target", edge.Directive.Target, declaredIn, "Declaration"),
        };

        // Shown as written, never evaluated - ADP does not have the variables, and a guess
        // would be a lie a reader could not detect (Requirement 5.7).
        if (edge.Directive.Condition.Length > 0)
        {
            rows.Add(Row("condition", "When", edge.Directive.Condition, declaredIn, "Declaration"));
        }

        if (edge.Resolution != AnsibleTargetResolution.Resolved)
        {
            rows.Add(Row(
                "resolution", "Resolves to",
                edge.Resolution == AnsibleTargetResolution.Missing
                    ? "nothing in this folder"
                    : "an expression, so it is not knowable without running Ansible",
                declaredIn, "Declaration"));
        }

        return rows;
    }

    // ---- plumbing ------------------------------------------------------------------------------

    /// <summary>
    /// One row, always read-only, with a reason naming the file the value lives in.
    /// </summary>
    /// <remarks>
    /// A single constructor for every row is what makes Requirement 10.2 true by construction:
    /// there is no path through this class that can produce a writable property, because there
    /// is no other way to make one.
    /// </remarks>
    private static ContextPropertyDefinition Row(string id, string label, string value, string source, string group) =>
        new(
            $"ansible.{id}",
            label,
            value,
            ContextPropertyEditor.Line,
            $"Defined in {source}; edit it in a text editor.",
            group);

    private static string Plays(AnsiblePlaybook playbook) =>
        playbook.Plays.Count == 1
            ? playbook.Plays[0].Name.Length > 0 ? playbook.Plays[0].Name : "1"
            : playbook.Plays.Count.ToString();

    private static string HostsOf(AnsiblePlaybook? playbook, AnsibleNode node)
    {
        if (playbook is null)
        {
            return "";
        }

        if (node.Kind == AnsibleNodeKind.Play)
        {
            var hash = node.Id.LastIndexOf('#');
            return hash >= 0 && int.TryParse(node.Id[(hash + 1)..], out var index)
                ? playbook.Plays.FirstOrDefault(play => play.Index == index)?.Hosts ?? ""
                : "";
        }

        return playbook.Plays.Count == 1 ? playbook.Plays[0].Hosts : "";
    }

    private static IEnumerable<(string Label, int Count, string Id)> Contents(AnsibleRoleContents contents) =>
    [
        ("Task files", contents.TaskFiles, "tasks"),
        ("Handlers", contents.Handlers, "handlers"),
        ("Templates", contents.Templates, "templates"),
        ("Files", contents.Files, "files"),
        ("Defaults", contents.Defaults, "defaults"),
        ("Vars", contents.Vars, "vars"),
    ];

    private static string DirectiveLabel(AnsibleDirectiveKind kind) => kind switch
    {
        AnsibleDirectiveKind.Roles => "roles:",
        AnsibleDirectiveKind.ImportRole => "import_role",
        AnsibleDirectiveKind.IncludeRole => "include_role",
        AnsibleDirectiveKind.ImportPlaybook => "import_playbook",
        AnsibleDirectiveKind.ImportTasks => "import_tasks",
        AnsibleDirectiveKind.IncludeTasks => "include_tasks",
        AnsibleDirectiveKind.Dependency => "dependencies",
        _ => "",
    };

    // ---- findings ----------------------------------------------------------------------------

    /// <summary>
    /// Host patterns that name something real without any inventory having to define it.
    /// Reporting these would fire on ordinary, correct playbooks - which is how a panel teaches
    /// people to stop reading it.
    /// </summary>
    private static readonly string[] ImplicitHosts = ["all", "*", "localhost", "127.0.0.1", "::1"];

    /// <summary>Everything wrong with <paramref name="project"/>, or none, as <c>AnsibleRuleSet.Judge</c> answered it.</summary>
    public static IReadOnlyList<DiagramProblem> Findings(AnsibleProject project)
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
