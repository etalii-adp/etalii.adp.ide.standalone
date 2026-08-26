namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The nodes and edges of one <see cref="AnsibleProject"/> - the relationships that are the
/// diagram (Requirement 5), resolved against what is actually in the folder.
/// </summary>
/// <remarks>
/// <para>
/// Pure: a project in, a graph out, no file access and no clock. Everything it needs was read
/// once by <see cref="AnsibleProjectReader"/>, which is why the diagram and the problems panel
/// can never disagree about what is in the folder.
/// </para>
/// <para>
/// <b>No per-variable usage edges.</b> Requirement 5.8 refuses them and so does this: variable
/// folders are nodes because their existence is a fact, while "this play reads that variable" is
/// an inference, and ansible-viz demonstrated that the inference guesses. A wrong edge is worse
/// than a missing one, because a reader cannot tell it is wrong.
/// </para>
/// </remarks>
public sealed class AnsibleGraph
{
    private AnsibleGraph(IReadOnlyList<AnsibleNode> nodes, IReadOnlyList<AnsibleEdge> edges)
    {
        Nodes = nodes;
        Edges = edges;
    }

    public IReadOnlyList<AnsibleNode> Nodes { get; }

    public IReadOnlyList<AnsibleEdge> Edges { get; }

    /// <summary>The node with that id, or null.</summary>
    public AnsibleNode? Node(string id) => Nodes.FirstOrDefault(node => string.Equals(node.Id, id, StringComparison.Ordinal));

    public static AnsibleGraph Derive(AnsibleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var nodes = new List<AnsibleNode>();
        var edges = new List<AnsibleEdge>();

        // A play gets a colour index in declaration order across the whole project, so two
        // playbooks' plays never share one (Requirement 6.3).
        var playIndex = 0;
        var playIndexOf = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var playbook in project.Playbooks)
        {
            nodes.Add(new AnsibleNode(PlaybookId(playbook), AnsibleNodeKind.Playbook, playbook.Name, playbook.RelativePath));

            // A play is its own box only when the playbook has more than one: a single-play
            // file would otherwise be drawn as two boxes saying the same thing (Requirement 4.1).
            var playsAreDrawn = playbook.Plays.Count > 1;
            foreach (var play in playbook.Plays)
            {
                var index = playIndex++;
                var owner = playsAreDrawn ? PlayId(playbook, play) : PlaybookId(playbook);
                playIndexOf[owner] = index;

                if (playsAreDrawn)
                {
                    nodes.Add(new AnsibleNode(
                        owner,
                        AnsibleNodeKind.Play,
                        play.Name.Length > 0 ? play.Name : $"play {play.Index + 1}",
                        playbook.RelativePath,
                        index));
                }
            }
        }

        foreach (var role in project.Roles)
        {
            nodes.Add(new AnsibleNode(RoleId(role.Name), AnsibleNodeKind.Role, role.Name, role.RelativePath));
        }

        foreach (var inventory in project.Inventories)
        {
            nodes.Add(new AnsibleNode(InventoryId(inventory), AnsibleNodeKind.Inventory, inventory.Name, inventory.RelativePath));
            foreach (var folder in inventory.VariableFolders)
            {
                nodes.Add(new AnsibleNode(VariableFolderId(folder), AnsibleNodeKind.VariableFolder, folder.Name, folder.RelativePath));
            }
        }

        // Task files are drawn only when something includes them (Requirement 4.1), so they are
        // added while the include edges are derived rather than up front.
        var drawnTaskFiles = new HashSet<string>(StringComparer.Ordinal);

        foreach (var playbook in project.Playbooks)
        {
            var playsAreDrawn = playbook.Plays.Count > 1;

            foreach (var import in playbook.Imports)
            {
                var target = ResolvePath(project, import.Target, playbook.RelativePath);
                edges.Add(new AnsibleEdge(
                    PlaybookId(playbook),
                    target is null ? "" : PlaybookId(target),
                    AnsibleEdgeKind.ImportsPlaybook,
                    Resolution(import, target is not null),
                    import));
            }

            foreach (var play in playbook.Plays)
            {
                var owner = playsAreDrawn ? PlayId(playbook, play) : PlaybookId(playbook);

                foreach (var directive in play.Directives)
                {
                    // A play's tasks may include another task file, but with no role to resolve
                    // it against there is nothing to point at; only role references are drawn.
                    if (directive.Kind is AnsibleDirectiveKind.IncludeTasks or AnsibleDirectiveKind.ImportTasks)
                    {
                        continue;
                    }

                    var role = directive.IsExpression ? null : project.Role(directive.Target);
                    edges.Add(new AnsibleEdge(
                        owner,
                        role is null ? "" : RoleId(role.Name),
                        AnsibleEdgeKind.UsesRole,
                        Resolution(directive, role is not null),
                        directive));
                }

                if (play.Hosts.Length > 0)
                {
                    edges.AddRange(TargetEdges(project, owner, play));
                }
            }
        }

        foreach (var role in project.Roles)
        {
            foreach (var dependency in role.Dependencies)
            {
                var target = dependency.IsExpression ? null : project.Role(dependency.Target);
                edges.Add(new AnsibleEdge(
                    RoleId(role.Name),
                    target is null ? "" : RoleId(target.Name),
                    AnsibleEdgeKind.DependsOn,
                    Resolution(dependency, target is not null),
                    dependency));
            }

            foreach (var include in role.TaskIncludes)
            {
                if (include.Kind is AnsibleDirectiveKind.IncludeRole or AnsibleDirectiveKind.ImportRole)
                {
                    // A role reaching for another role is the same relationship a play's
                    // roles: list declares, so it is drawn the same way.
                    var used = include.IsExpression ? null : project.Role(include.Target);
                    edges.Add(new AnsibleEdge(
                        RoleId(role.Name),
                        used is null ? "" : RoleId(used.Name),
                        AnsibleEdgeKind.UsesRole,
                        Resolution(include, used is not null),
                        include));
                    continue;
                }

                var file = include.IsExpression ? null : ResolveTaskFile(role, include);
                if (file is not null && drawnTaskFiles.Add(file.RelativePath))
                {
                    nodes.Add(new AnsibleNode(TaskFileId(file), AnsibleNodeKind.TaskFile, file.Name, file.RelativePath));
                }

                edges.Add(new AnsibleEdge(
                    RoleId(role.Name),
                    file is null ? "" : TaskFileId(file),
                    AnsibleEdgeKind.IncludesTasks,
                    Resolution(include, file is not null),
                    include));
            }
        }

        return new AnsibleGraph(nodes, edges);
    }

    /// <summary>
    /// A play's <c>hosts:</c> pattern against every inventory that defines a matching group -
    /// one edge per inventory (Requirement 5.6). A pattern nothing defines yields no edge at
    /// all; the rule set reports it instead.
    /// </summary>
    private static IEnumerable<AnsibleEdge> TargetEdges(AnsibleProject project, string owner, AnsiblePlay play)
    {
        var directive = new AnsibleDirective(AnsibleDirectiveKind.Roles, play.Hosts, "", "", play.Line);
        foreach (var inventory in project.Inventories)
        {
            if (Matches(play.Hosts, inventory))
            {
                yield return new AnsibleEdge(
                    owner,
                    InventoryId(inventory),
                    AnsibleEdgeKind.Targets,
                    AnsibleTargetResolution.Resolved,
                    directive);
            }
        }
    }

    /// <summary>
    /// Whether a host pattern names anything this inventory defines.
    /// </summary>
    /// <remarks>
    /// A deliberate subset of Ansible's pattern language: union separators, <c>all</c>/<c>*</c>,
    /// and a trailing glob. Exclusions (<c>!group</c>) and intersections (<c>&amp;group</c>) do
    /// not create an edge - an exclusion names what a play will <em>not</em> run on, and drawing
    /// a line for it would say the opposite of what the file says.
    /// </remarks>
    public static bool Matches(string? pattern, AnsibleInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        foreach (var token in (pattern ?? "").Split([':', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (token.StartsWith('!') || token.StartsWith('&'))
            {
                continue;
            }

            if (token is "all" or "*")
            {
                // Every inventory defines "all" implicitly, but only if it has anything in it.
                if (inventory.Groups.Count > 0)
                {
                    return true;
                }
                continue;
            }

            if (inventory.Groups.Any(group => NameMatches(token, group.Name)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NameMatches(string token, string group) =>
        token.EndsWith('*')
            ? group.StartsWith(token[..^1], StringComparison.Ordinal)
            : string.Equals(token, group, StringComparison.Ordinal);

    private static AnsibleTargetResolution Resolution(AnsibleDirective directive, bool found) =>
        directive.IsExpression ? AnsibleTargetResolution.Unresolvable
        : found ? AnsibleTargetResolution.Resolved
        : AnsibleTargetResolution.Missing;

    /// <summary>The playbook a path names, resolved relative to the file that named it.</summary>
    private static AnsiblePlaybook? ResolvePath(AnsibleProject project, string target, string declaringFile)
    {
        var folder = Folder(declaringFile);
        var combined = Normalise(folder.Length == 0 ? target : $"{folder}/{target}");
        return project.Playbooks.FirstOrDefault(playbook =>
            string.Equals(playbook.RelativePath, combined, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The task file an include names, resolved relative to the file that included it.</summary>
    private static AnsibleTaskFile? ResolveTaskFile(AnsibleRole role, AnsibleDirective include)
    {
        var folder = Folder(include.DeclaredIn);
        var combined = Normalise(folder.Length == 0 ? include.Target : $"{folder}/{include.Target}");
        return role.TaskFiles.FirstOrDefault(file =>
            string.Equals(file.RelativePath, combined, StringComparison.OrdinalIgnoreCase));
    }

    private static string Folder(string relativePath)
    {
        var slash = relativePath.LastIndexOf('/');
        return slash < 0 ? "" : relativePath[..slash];
    }

    /// <summary>Collapses <c>.</c> and <c>..</c> segments without touching the disk.</summary>
    private static string Normalise(string path)
    {
        var segments = new List<string>();
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }
            if (segment == ".." && segments.Count > 0 && segments[^1] != "..")
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }
            segments.Add(segment);
        }
        return string.Join('/', segments);
    }

    private static string PlaybookId(AnsiblePlaybook playbook) => $"playbook:{playbook.RelativePath}";

    private static string PlayId(AnsiblePlaybook playbook, AnsiblePlay play) => $"play:{playbook.RelativePath}#{play.Index}";

    private static string RoleId(string name) => $"role:{name}";

    private static string TaskFileId(AnsibleTaskFile file) => $"taskfile:{file.RelativePath}";

    private static string InventoryId(AnsibleInventory inventory) => $"inventory:{inventory.RelativePath}";

    private static string VariableFolderId(AnsibleVariableFolder folder) => $"vars:{folder.RelativePath}";
}
