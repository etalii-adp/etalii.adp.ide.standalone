using Serilog;

using YamlDotNet.RepresentationModel;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Reads one registered folder into an <see cref="AnsibleProject"/>, recognising Ansible's own
/// conventions and ignoring everything else without complaint.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing here opens a file for writing.</b> There is no code path that could, which is how
/// Requirement 1.1 is satisfied by construction rather than by discipline.
/// </para>
/// <para>
/// <b>Recognition is by convention, never by a name list ADP invented.</b> A playbook is a YAML
/// file whose top level is a list of mappings - the shape a play has - which is what lets a
/// data file living in the same folder exclude itself without anyone maintaining an exception.
/// </para>
/// <para>
/// <b>Every enumeration is sorted before use.</b> <see cref="Directory.EnumerateFiles(string)"/>
/// returns filesystem order, which is not a promise; determinism is a requirement, so the order
/// is made ordinal here rather than hoped for.
/// </para>
/// </remarks>
public sealed class AnsibleProjectReader
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleProjectReader>();

    /// <summary>Files whose presence is recorded on the project rather than drawn as a box (Requirement 4.1).</summary>
    private static readonly string[] AnnotationNames = ["ansible.cfg", "requirements.yml", "requirements.yaml"];

    /// <summary>A root inventory, for a project that keeps one file rather than an inventories folder.</summary>
    private static readonly string[] RootInventoryNames = ["inventory", "inventory.yml", "inventory.yaml", "hosts", "hosts.yml", "hosts.yaml"];

    /// <summary>Reads <paramref name="folder"/>. A folder that is not there reads as an empty project, not an error.</summary>
    public AnsibleProject Read(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var root = IoPath.GetFullPath(folder);
        if (!Directory.Exists(root))
        {
            _logger.Debug("Nothing to read: {Folder} is not there", root);
            return Empty(root);
        }

        var failures = new List<AnsibleYamlFailure>();
        var playbooks = ReadPlaybooks(root, failures);
        var roles = ReadRoles(root, failures);
        var inventories = ReadInventories(root, failures);
        var annotations = ReadAnnotations(root);

        return new AnsibleProject(root, playbooks, roles, inventories, annotations, failures);
    }

    private static AnsibleProject Empty(string root) => new(root, [], [], [], [], []);

    // ---- playbooks -----------------------------------------------------------------------

    private static IReadOnlyList<AnsiblePlaybook> ReadPlaybooks(string root, List<AnsibleYamlFailure> failures)
    {
        // Root level and playbooks/ - the two places the recommended layout puts them. A team
        // that keeps theirs in plays/ gets an undrawn folder, not a problem (Requirement 9.4).
        var candidates = YamlFilesIn(root)
            .Concat(YamlFilesIn(IoPath.Combine(root, "playbooks")))
            .ToArray();

        var playbooks = new List<AnsiblePlaybook>();
        foreach (var path in candidates)
        {
            var relative = Relative(root, path);
            var result = AnsibleYaml.Read(path, relative);
            if (result is not AnsibleYamlDocument document)
            {
                Record(result, failures);
                continue;
            }

            // The shape test that identifies a playbook, and the whole of it: a list whose
            // entries are mappings. A file that is a mapping at the top - inventory data, a
            // variables file, someone's notes - is simply not one.
            if (document.Root is not YamlSequenceNode sequence ||
                sequence.Children.Count == 0 ||
                !sequence.Children.All(child => child is YamlMappingNode))
            {
                continue;
            }

            var plays = new List<AnsiblePlay>();
            var imports = new List<AnsibleDirective>();
            var index = 0;
            foreach (var entry in sequence.Children.OfType<YamlMappingNode>())
            {
                // import_playbook is a sibling of the plays, not something a play contains.
                if (Scalar(entry, "import_playbook") is { } imported)
                {
                    imports.Add(new AnsibleDirective(
                        AnsibleDirectiveKind.ImportPlaybook, imported, Condition(entry), relative, Line(entry)));
                    continue;
                }

                plays.Add(ReadPlay(entry, index++, relative));
            }

            playbooks.Add(new AnsiblePlaybook(relative, IoPath.GetFileName(path), plays, imports));
        }

        return playbooks;
    }

    private static AnsiblePlay ReadPlay(YamlMappingNode play, int index, string relative)
    {
        var directives = new List<AnsibleDirective>();

        // roles: is a list whose entries are either a name or a mapping with role: and a when:.
        if (play.Children.TryGetValue(new YamlScalarNode("roles"), out var roles) && roles is YamlSequenceNode list)
        {
            foreach (var entry in list.Children)
            {
                switch (entry)
                {
                    case YamlScalarNode { Value: { } name }:
                        directives.Add(new AnsibleDirective(AnsibleDirectiveKind.Roles, name, "", relative, Line(entry)));
                        break;

                    case YamlMappingNode mapping when Scalar(mapping, "role") is { } named:
                        directives.Add(new AnsibleDirective(
                            AnsibleDirectiveKind.Roles, named, Condition(mapping), relative, Line(mapping)));
                        break;
                }
            }
        }

        // A play's tasks may also reach for a role directly.
        directives.AddRange(TaskDirectives(play, relative));

        return new AnsiblePlay(
            Scalar(play, "name") ?? "",
            Scalar(play, "hosts") ?? "",
            index,
            Line(play),
            directives);
    }

    // ---- roles ---------------------------------------------------------------------------

    private static IReadOnlyList<AnsibleRole> ReadRoles(string root, List<AnsibleYamlFailure> failures)
    {
        var rolesFolder = IoPath.Combine(root, "roles");
        if (!Directory.Exists(rolesFolder))
        {
            return [];
        }

        var roles = new List<AnsibleRole>();
        foreach (var folder in SortedDirectories(rolesFolder))
        {
            var name = IoPath.GetFileName(folder);
            var taskFiles = YamlFilesIn(IoPath.Combine(folder, "tasks"))
                .Select(path => new AnsibleTaskFile(IoPath.GetFileName(path), Relative(root, path)))
                .ToArray();

            var contents = new AnsibleRoleContents(
                TaskFiles: taskFiles.Length,
                Handlers: CountIn(IoPath.Combine(folder, "handlers")),
                Templates: CountIn(IoPath.Combine(folder, "templates")),
                Files: CountIn(IoPath.Combine(folder, "files")),
                Defaults: CountIn(IoPath.Combine(folder, "defaults")),
                Vars: CountIn(IoPath.Combine(folder, "vars")),
                HasMeta: Directory.Exists(IoPath.Combine(folder, "meta")),
                HasLibrary: Directory.Exists(IoPath.Combine(folder, "library")));

            roles.Add(new AnsibleRole(
                name,
                Relative(root, folder),
                contents,
                taskFiles,
                ReadDependencies(root, folder, failures),
                ReadTaskIncludes(root, folder, failures)));
        }

        return roles;
    }

    private static IReadOnlyList<AnsibleDirective> ReadDependencies(string root, string roleFolder, List<AnsibleYamlFailure> failures)
    {
        var meta = FirstExisting(IoPath.Combine(roleFolder, "meta"), "main.yml", "main.yaml");
        if (meta is null)
        {
            return [];
        }

        var relative = Relative(root, meta);
        var result = AnsibleYaml.Read(meta, relative);
        if (result is not AnsibleYamlDocument { Root: YamlMappingNode mapping })
        {
            Record(result, failures);
            return [];
        }

        if (!mapping.Children.TryGetValue(new YamlScalarNode("dependencies"), out var dependencies) ||
            dependencies is not YamlSequenceNode list)
        {
            return [];
        }

        var directives = new List<AnsibleDirective>();
        foreach (var entry in list.Children)
        {
            // Both shapes Ansible accepts: a bare name, or a mapping naming role: (or name:).
            var target = entry switch
            {
                YamlScalarNode { Value: { } value } => value,
                YamlMappingNode nested => Scalar(nested, "role") ?? Scalar(nested, "name"),
                _ => null,
            };

            if (target is not null)
            {
                directives.Add(new AnsibleDirective(
                    AnsibleDirectiveKind.Dependency, target, Condition(entry as YamlMappingNode), relative, Line(entry)));
            }
        }

        return directives;
    }

    private static IReadOnlyList<AnsibleDirective> ReadTaskIncludes(string root, string roleFolder, List<AnsibleYamlFailure> failures)
    {
        var directives = new List<AnsibleDirective>();
        foreach (var path in YamlFilesIn(IoPath.Combine(roleFolder, "tasks")))
        {
            var relative = Relative(root, path);
            var result = AnsibleYaml.Read(path, relative);
            if (result is not AnsibleYamlDocument { Root: YamlSequenceNode tasks })
            {
                Record(result, failures);
                continue;
            }

            foreach (var task in tasks.Children.OfType<YamlMappingNode>())
            {
                directives.AddRange(TaskDirectives(task, relative));
            }
        }

        return directives;
    }

    /// <summary>
    /// The include and import directives one task mapping carries. Both the short form
    /// (<c>include_tasks: x.yml</c>) and the fully-qualified one
    /// (<c>ansible.builtin.include_tasks:</c>), because real playbooks use both and the module
    /// would be reading half a repository if it only understood one.
    /// </summary>
    private static IEnumerable<AnsibleDirective> TaskDirectives(YamlMappingNode task, string relative)
    {
        foreach ((string suffix, AnsibleDirectiveKind kind) in DirectiveNames)
        {
            foreach ((YamlNode key, YamlNode value) in task.Children)
            {
                if (key is not YamlScalarNode { Value: { } name } || !Matches(name, suffix))
                {
                    continue;
                }

                // include_tasks: x.yml, or include_tasks: { file: x.yml } / { name: x } for a role.
                var target = value switch
                {
                    YamlScalarNode { Value: { } scalar } => scalar,
                    YamlMappingNode mapping => Scalar(mapping, "file") ?? Scalar(mapping, "name"),
                    _ => null,
                };

                if (target is not null)
                {
                    yield return new AnsibleDirective(kind, target, Condition(task), relative, Line(key));
                }
            }
        }
    }

    private static readonly (string Suffix, AnsibleDirectiveKind Kind)[] DirectiveNames =
    [
        ("include_tasks", AnsibleDirectiveKind.IncludeTasks),
        ("import_tasks", AnsibleDirectiveKind.ImportTasks),
        ("include_role", AnsibleDirectiveKind.IncludeRole),
        ("import_role", AnsibleDirectiveKind.ImportRole),
    ];

    /// <summary>Matches <c>include_tasks</c> and <c>ansible.builtin.include_tasks</c> alike, and nothing else.</summary>
    private static bool Matches(string key, string suffix) =>
        string.Equals(key, suffix, StringComparison.Ordinal) ||
        (key.EndsWith(suffix, StringComparison.Ordinal) &&
         key.Length > suffix.Length &&
         key[key.Length - suffix.Length - 1] == '.');

    // ---- inventories ---------------------------------------------------------------------

    private static IReadOnlyList<AnsibleInventory> ReadInventories(string root, List<AnsibleYamlFailure> failures)
    {
        var inventories = new List<AnsibleInventory>();

        var folder = IoPath.Combine(root, "inventories");
        if (Directory.Exists(folder))
        {
            foreach (var environment in SortedDirectories(folder))
            {
                inventories.Add(new AnsibleInventory(
                    IoPath.GetFileName(environment),
                    Relative(root, environment),
                    GroupsIn(root, environment, failures),
                    VariableFoldersIn(root, environment)));
            }
        }

        // A project that keeps one inventory file at its root rather than a folder per
        // environment. Its group_vars and host_vars, if any, sit beside it at the root.
        foreach (var name in RootInventoryNames)
        {
            var path = IoPath.Combine(root, name);
            if (!File.Exists(path))
            {
                continue;
            }

            inventories.Add(new AnsibleInventory(
                name,
                Relative(root, path),
                GroupsOf(root, path, failures),
                VariableFoldersIn(root, root)));
        }

        return inventories;
    }

    private static IReadOnlyList<AnsibleInventoryGroup> GroupsIn(string root, string folder, List<AnsibleYamlFailure> failures)
    {
        var groups = new List<AnsibleInventoryGroup>();
        foreach (var path in SortedFiles(folder).Where(IsInventoryFile))
        {
            groups.AddRange(GroupsOf(root, path, failures));
        }
        return groups;
    }

    private static IReadOnlyList<AnsibleInventoryGroup> GroupsOf(string root, string path, List<AnsibleYamlFailure> failures)
    {
        var relative = Relative(root, path);
        var result = AnsibleYaml.Read(path, relative);
        if (result is not AnsibleYamlDocument { Root: YamlMappingNode mapping })
        {
            // An INI-format inventory is not YAML and will not parse. That is not the user's
            // mistake, so it is not recorded as a failure - it is read as the INI it is,
            // because a rule that answers "no inventory defines it" over groups the file
            // plainly defines is a well-formed wrong answer.
            return IniGroupsOf(path);
        }

        var groups = new List<AnsibleInventoryGroup>();
        // The YAML inventory shape: all -> children -> <group> -> hosts -> <host>.
        foreach ((YamlNode topKey, YamlNode topValue) in mapping.Children)
        {
            if (topValue is not YamlMappingNode top)
            {
                continue;
            }

            if (top.Children.TryGetValue(new YamlScalarNode("children"), out var children) &&
                children is YamlMappingNode nested)
            {
                foreach ((YamlNode groupKey, YamlNode groupValue) in nested.Children)
                {
                    if (groupKey is YamlScalarNode { Value: { } group })
                    {
                        groups.Add(new AnsibleInventoryGroup(group, HostCount(groupValue)));
                    }
                }
            }

            // A group declared at the top level rather than under all -> children.
            if (top.Children.ContainsKey(new YamlScalarNode("hosts")) &&
                topKey is YamlScalarNode { Value: { } name } &&
                !string.Equals(name, "all", StringComparison.Ordinal))
            {
                groups.Add(new AnsibleInventoryGroup(name, HostCount(top)));
            }
        }

        _ = failures; // Nothing here is the user's mistake to hear about.
        return groups;
    }

    private static int HostCount(YamlNode group) =>
        group is YamlMappingNode mapping &&
        mapping.Children.TryGetValue(new YamlScalarNode("hosts"), out var hosts) &&
        hosts is YamlMappingNode named
            ? named.Children.Count
            : 0;

    private static IReadOnlyList<AnsibleVariableFolder> VariableFoldersIn(string root, string owner)
    {
        var folders = new List<AnsibleVariableFolder>();
        foreach (var name in (string[])["group_vars", "host_vars"])
        {
            var path = IoPath.Combine(owner, name);
            if (Directory.Exists(path))
            {
                folders.Add(new AnsibleVariableFolder(name, Relative(root, path), CountIn(path)));
            }
        }
        return folders;
    }

    // ---- annotations and plumbing --------------------------------------------------------

    private static IReadOnlyList<string> ReadAnnotations(string root)
    {
        var annotations = new List<string>();
        foreach (var name in AnnotationNames)
        {
            var path = IoPath.Combine(root, name);
            if (File.Exists(path))
            {
                annotations.Add(Relative(root, path));
            }
        }

        // collections/requirements.yml, the Galaxy convention.
        foreach (var name in (string[])["requirements.yml", "requirements.yaml"])
        {
            var path = IoPath.Combine(root, "collections", name);
            if (File.Exists(path))
            {
                annotations.Add(Relative(root, path));
            }
        }

        return annotations;
    }

    private static void Record(AnsibleYamlResult result, List<AnsibleYamlFailure> failures)
    {
        if (result is AnsibleYamlUnreadable unreadable &&
            !failures.Any(failure => string.Equals(failure.RelativePath, unreadable.Failure.RelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(unreadable.Failure);
        }
    }

    private static string? Scalar(YamlMappingNode? mapping, string key) =>
        mapping is not null &&
        mapping.Children.TryGetValue(new YamlScalarNode(key), out var value) &&
        value is YamlScalarNode { Value: { } scalar }
            ? scalar
            : null;

    /// <summary>A <c>when:</c> as written. A list of conditions is joined the way a reader would read it.</summary>
    private static string Condition(YamlMappingNode? mapping)
    {
        if (mapping is null || !mapping.Children.TryGetValue(new YamlScalarNode("when"), out var when))
        {
            return "";
        }

        return when switch
        {
            YamlScalarNode { Value: { } value } => value,
            YamlSequenceNode list => string.Join(" and ", list.Children.OfType<YamlScalarNode>().Select(child => child.Value)),
            _ => "",
        };
    }

    private static uint Line(YamlNode node) => (uint)Math.Max(node.Start.Line, 0);

    private static string Relative(string root, string path) => IoPath.GetRelativePath(root, path).Replace('\\', '/');

    /// <summary>
    /// The classic INI inventory: <c>[group]</c> sections holding one host per line,
    /// <c>[group:children]</c> gathering other groups, <c>[group:vars]</c> holding
    /// configuration rather than membership. A range line (<c>web[01:50].example.com</c>)
    /// counts as one entry - the group's existence is what the rules need, the count is a
    /// summary.
    /// </summary>
    private static IReadOnlyList<AnsibleInventoryGroup> IniGroupsOf(string path)
    {
        var hostCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        string? section = null;
        var sectionIsChildren = false;

        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                var name = line[1..^1];
                var colon = name.IndexOf(':');
                var suffix = colon < 0 ? "" : name[(colon + 1)..];
                name = colon < 0 ? name : name[..colon];
                if (suffix == "vars")
                {
                    section = null;
                    continue;
                }

                sectionIsChildren = suffix == "children";
                section = name;
                if (!hostCounts.ContainsKey(name))
                {
                    hostCounts[name] = 0;
                    order.Add(name);
                }
                continue;
            }

            if (section is null)
            {
                continue; // An ungrouped host above the first section belongs to no group.
            }

            var entry = line.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries)[0];
            if (sectionIsChildren)
            {
                (childrenOf.TryGetValue(section, out var children)
                    ? children
                    : childrenOf[section] = []).Add(entry);
            }
            else
            {
                hostCounts[section]++;
            }
        }

        // A :children group holds what its members hold. One level of gathering is resolved
        // here; a child that is itself a :children group contributes its own direct hosts only,
        // which keeps this a summary rather than a graph traversal.
        foreach ((string group, List<string> children) in childrenOf)
        {
            hostCounts[group] += children.Sum(hostCounts.GetValueOrDefault);
        }

        return order.Select(name => new AnsibleInventoryGroup(name, hostCounts[name])).ToArray();
    }

    private static bool IsInventoryFile(string path) =>
        IoPath.GetExtension(path) is ".yml" or ".yaml" or "" ||
        RootInventoryNames.Contains(IoPath.GetFileName(path), StringComparer.OrdinalIgnoreCase);

    private static string? FirstExisting(string folder, params string[] names) =>
        names.Select(name => IoPath.Combine(folder, name)).FirstOrDefault(File.Exists);

    private static int CountIn(string folder) => Directory.Exists(folder) ? SortedFiles(folder).Length : 0;

    private static string[] YamlFilesIn(string folder) =>
        Directory.Exists(folder)
            ? [.. SortedFiles(folder).Where(path => IoPath.GetExtension(path) is ".yml" or ".yaml")]
            : [];

    /// <summary>Ordinal, always: the filesystem's own order is not a promise and determinism is a requirement.</summary>
    private static string[] SortedFiles(string folder)
    {
        try
        {
            return [.. Directory.EnumerateFiles(folder).Order(StringComparer.Ordinal)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Skipping {Folder}: it could not be listed", folder);
            return [];
        }
    }

    /// <summary>
    /// Subfolders, ordinally, with reparse points that leave their parent left alone - the
    /// technique <c>ProblemCollector.MarkFolderVisited</c> uses, for the same reason: a symlink
    /// loop must end the walk rather than the process.
    /// </summary>
    private static string[] SortedDirectories(string folder)
    {
        try
        {
            return
            [
                .. Directory.EnumerateDirectories(folder)
                    .Where(child => (new DirectoryInfo(child).Attributes & FileAttributes.ReparsePoint) == 0)
                    .Order(StringComparer.Ordinal),
            ];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _logger.Debug(exception, "Skipping {Folder}: it could not be listed", folder);
            return [];
        }
    }
}
