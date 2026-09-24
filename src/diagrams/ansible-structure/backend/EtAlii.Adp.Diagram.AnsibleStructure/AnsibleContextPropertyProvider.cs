using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.Context;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// What the property grid shows for a selected Ansible element - everything, and none of it
/// editable (Requirement 10).
/// </summary>
/// <remarks>
/// <para>
/// This is property-grid Requirement 4 exercised at 100%: <b>every</b> row carries a non-empty
/// <see cref="ContextPropertyDefinition.ReadOnlyReason"/>, and every reason names the file the
/// value actually lives in and how it is edited. Not one property here is writable, which makes
/// this the first provider whose read-only markings are the whole of its behaviour rather than
/// an exception to it.
/// </para>
/// <para>
/// "Read-only" here is enforced rather than styled: <c>ContextPropertyResolver</c> refuses a
/// write to a property its owner described as read-only, server-side, whatever the client
/// claimed. <see cref="SetAsync"/> is therefore unreachable in practice - and is written to
/// refuse anyway, because a provider that would silently accept a write if the resolver ever
/// changed is a trap rather than a design.
/// </para>
/// <para>
/// The reasons follow the shape the mindmap's two read-only rows established: cause first, then
/// the remedy. "Defined in X; edit it in a text editor." A reader who cannot change a value here
/// deserves to be told what would have to change instead.
/// </para>
/// </remarks>
public sealed class AnsibleContextPropertyProvider : IContextPropertyProvider
{
    private const string Refusal =
        "An Ansible structure diagram shows the folder as it is and changes nothing in it. " +
        "Edit the file this value comes from in a text editor.";

    private readonly IAnsibleProjectStore _store;

    public AnsibleContextPropertyProvider(IAnsibleProjectStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    public ContextScope Scope => ContextScope.DiagramElement;

    public ValueTask<IReadOnlyList<ContextPropertyDefinition>> DescribeAsync(ContextTarget target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        cancellationToken.ThrowIfCancellationRequested();

        if (target.ElementId.Length == 0 || !Directory.Exists(target.ResolvedFullPath))
        {
            return Empty;
        }

        var project = _store.GetOrLoad(target.ResolvedFullPath);
        var graph = AnsibleGraph.Derive(project);

        var node = graph.Node(target.ElementId);
        if (node is not null)
        {
            return ValueTask.FromResult(Describe(project, graph, node));
        }

        var edge = graph.Edges.FirstOrDefault(candidate => string.Equals(candidate.Id, target.ElementId, StringComparison.Ordinal));
        return edge is null ? Empty : ValueTask.FromResult(Describe(edge));
    }

    /// <summary>
    /// Refuses, always, with the property's own reason.
    /// </summary>
    /// <remarks>
    /// Unreachable through the grid - <c>ContextPropertyResolver</c> stops a write to a
    /// read-only property before a provider is consulted - and deliberately written all the
    /// same. A refusal, never an exception: the same discipline <c>CommandResult</c> imposes.
    /// </remarks>
    public ValueTask<ContextPropertyResult> SetAsync(
        ContextTarget target, string propertyId, string value, CancellationToken cancellationToken) =>
        ValueTask.FromResult(ContextPropertyResult.Failure(Refusal));

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
            foreach (var (label, count, id) in Contents(role.Contents))
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

    private static readonly ValueTask<IReadOnlyList<ContextPropertyDefinition>> Empty =
        ValueTask.FromResult<IReadOnlyList<ContextPropertyDefinition>>([]);
}
