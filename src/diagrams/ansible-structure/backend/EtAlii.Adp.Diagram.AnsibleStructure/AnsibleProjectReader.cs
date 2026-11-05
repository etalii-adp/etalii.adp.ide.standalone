using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;
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
/// <b>It reads the files it is handed</b> (ansible-structure-disl-fbl, Requirement 4.2): the files the
/// binding's file rules select (<c>ansible-structure.fbl</c>), by their relative paths and bytes, and what
/// <see cref="AnsibleFolderListing"/> says of the folder that no file can: which folders are there, how
/// many files they hold, and the files that are links. It opens no file itself.
/// <see cref="AnsibleFolderPlugin"/> is FBL's <c>read</c> over it.
/// </para>
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
/// <b>Every enumeration is sorted before use</b>, ordinally, and in the reader's own order: the
/// root's playbooks, then <c>playbooks/</c>, then roles, then inventories (finding B6), whatever order
/// the files were handed in.
/// </para>
/// </remarks>
public sealed class AnsibleProjectReader
{
    private static readonly ILogger _logger = Log.ForContext<AnsibleProjectReader>();

    private static readonly StringComparer Paths = Glob.PlatformIgnoresCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Files whose presence is recorded on the project rather than drawn as a box (Requirement 4.1).</summary>
    private static readonly string[] AnnotationNames = ["ansible.cfg", "requirements.yml", "requirements.yaml"];

    /// <summary>A root inventory, for a project that keeps one file rather than an inventories folder.</summary>
    private static readonly string[] RootInventoryNames = ["inventory", "inventory.yml", "inventory.yaml", "hosts", "hosts.yml", "hosts.yaml"];

    /// <summary>
    /// Reads <paramref name="folder"/>: selects its files through the binding (<see cref="FolderSubject.Files"/>),
    /// reads them shared and read-only as the folder body does, and reads what it selected. A folder that
    /// is not there reads as an empty project, not an error.
    /// </summary>
    public AnsibleProject Read(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);

        var listing = new AnsibleFolderListing(folder);
        try
        {
            if (listing.FolderExists(""))
            {
                return Read(Selected(listing.Root), listing);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Removed while it was listed: what is not there reads as nothing.
        }

        _logger.Debug("Nothing to read: {Folder} is not there", listing.Root);
        return Empty(listing.Root);
    }

    /// <summary>
    /// Reads the folder <paramref name="listing"/> lists from <paramref name="files"/>, the files the binding
    /// selects with their bytes, and from the listing.
    /// </summary>
    public AnsibleProject Read(IReadOnlyList<PluginFile> files, AnsibleFolderListing listing)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(listing);

        var handed = new Dictionary<string, PluginFile>(Paths);
        foreach (var file in files.Concat(listing.LinkedFiles(AnsibleDefinition.Binding)))
        {
            handed.TryAdd(file.RelativePath, file);
        }

        var reading = new Reading(handed, listing);
        var failures = new List<TolerantYamlFailure>();
        var playbooks = ReadPlaybooks(reading, failures);
        var roles = ReadRoles(reading, failures);
        var inventories = ReadInventories(reading, failures);
        var annotations = ReadAnnotations(reading);

        return new AnsibleProject(listing.Root, playbooks, roles, inventories, annotations, failures);
    }

    /// <summary>The files the binding selects in <paramref name="root"/>, read as the folder body reads them, so a reading here is the plugin's.</summary>
    private static List<PluginFile> Selected(string root)
    {
        var options = new FblOptions();
        return [.. FolderSubject.Files(AnsibleDefinition.Binding, root).Select(file => Load(file, options))];
    }

    /// <summary>Read-only and shared, so a concurrent save is never refused; a file over the host's limit is handed with no bytes.</summary>
    private static PluginFile Load(FolderFile file, FblOptions options)
    {
        using var stream = new FileStream(file.FullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > options.MaxBodyBytes) return new PluginFile(file.RelativePath, []) { IsOversized = true };
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return new PluginFile(file.RelativePath, buffer.ToArray());
    }

    private static AnsibleProject Empty(string root) => new(root, [], [], [], [], []);

    /// <summary>The handed files and the listing of one reading: where the reader looks in place of the disk.</summary>
    private sealed class Reading(Dictionary<string, PluginFile> handed, AnsibleFolderListing listing)
    {
        public AnsibleFolderListing Listing { get; } = listing;

        /// <summary>
        /// The handed files directly in the folder at <paramref name="folder"/>, in ordinal order, each named by
        /// that folder as written here and its own name, as a listing of that folder names them.
        /// </summary>
        public IReadOnlyList<(string Relative, PluginFile File)> FilesIn(string folder) =>
        [
            .. handed.Values
                .Where(file => Paths.Equals(Parent(file.RelativePath), folder))
                .Select(file => (Relative: Join(folder, Name(file.RelativePath)), File: file))
                .OrderBy(entry => entry.Relative, StringComparer.Ordinal),
        ];

        /// <summary>The handed file at <paramref name="relative"/>, or null.</summary>
        public PluginFile? Find(string relative) => handed.GetValueOrDefault(relative);

        private static string Parent(string relative) => relative.LastIndexOf('/') is var slash and >= 0 ? relative[..slash] : "";

        private static string Name(string relative) => relative[(relative.LastIndexOf('/') + 1)..];
    }

    private static string Join(string folder, string name) => folder.Length == 0 ? name : folder + "/" + name;

    // ---- playbooks -----------------------------------------------------------------------

    private static IReadOnlyList<AnsiblePlaybook> ReadPlaybooks(Reading reading, List<TolerantYamlFailure> failures)
    {
        // Root level and playbooks/ - the two places the recommended layout puts them. A team
        // that keeps theirs in plays/ gets an undrawn folder, not a problem (Requirement 9.4).
        var candidates = YamlFilesIn(reading, "")
            .Concat(YamlFilesIn(reading, "playbooks"))
            .ToArray();

        var playbooks = new List<AnsiblePlaybook>();
        foreach ((string relative, PluginFile file) in candidates)
        {
            var result = PluginFileYaml.Read(file, relative);
            if (result is not TolerantYamlDocument document)
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

            playbooks.Add(new AnsiblePlaybook(relative, IoPath.GetFileName(relative), plays, imports));
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

    private static IReadOnlyList<AnsibleRole> ReadRoles(Reading reading, List<TolerantYamlFailure> failures)
    {
        var listing = reading.Listing;
        if (!listing.FolderExists("roles"))
        {
            return [];
        }

        var roles = new List<AnsibleRole>();
        foreach (var name in listing.Folders("roles"))
        {
            var folder = "roles/" + name;
            var taskFiles = YamlFilesIn(reading, folder + "/tasks")
                .Select(entry => new AnsibleTaskFile(IoPath.GetFileName(entry.Relative), entry.Relative))
                .ToArray();

            var contents = new AnsibleRoleContents(
                TaskFiles: taskFiles.Length,
                Handlers: listing.FileCount(folder + "/handlers"),
                Templates: listing.FileCount(folder + "/templates"),
                Files: listing.FileCount(folder + "/files"),
                Defaults: listing.FileCount(folder + "/defaults"),
                Vars: listing.FileCount(folder + "/vars"),
                HasMeta: listing.FolderExists(folder + "/meta"),
                HasLibrary: listing.FolderExists(folder + "/library"));

            roles.Add(new AnsibleRole(
                name,
                folder,
                contents,
                taskFiles,
                ReadDependencies(reading, folder, failures),
                ReadTaskIncludes(reading, folder, failures)));
        }

        return roles;
    }

    private static IReadOnlyList<AnsibleDirective> ReadDependencies(Reading reading, string roleFolder, List<TolerantYamlFailure> failures)
    {
        var meta = ((string[])["main.yml", "main.yaml"])
            .Select(name => roleFolder + "/meta/" + name)
            .Select(relative => (Relative: relative, File: reading.Find(relative)))
            .FirstOrDefault(candidate => candidate.File is not null);
        if (meta.File is null)
        {
            return [];
        }

        var relative = meta.Relative;
        var result = PluginFileYaml.Read(meta.File, relative);
        if (result is not TolerantYamlDocument { Root: YamlMappingNode mapping })
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

    private static IReadOnlyList<AnsibleDirective> ReadTaskIncludes(Reading reading, string roleFolder, List<TolerantYamlFailure> failures)
    {
        var directives = new List<AnsibleDirective>();
        foreach ((string relative, PluginFile file) in YamlFilesIn(reading, roleFolder + "/tasks"))
        {
            var result = PluginFileYaml.Read(file, relative);
            if (result is not TolerantYamlDocument { Root: YamlSequenceNode tasks })
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

    private static IReadOnlyList<AnsibleInventory> ReadInventories(Reading reading, List<TolerantYamlFailure> failures)
    {
        var inventories = new List<AnsibleInventory>();

        if (reading.Listing.FolderExists("inventories"))
        {
            foreach (var name in reading.Listing.Folders("inventories"))
            {
                var environment = "inventories/" + name;
                inventories.Add(new AnsibleInventory(
                    name,
                    environment,
                    GroupsIn(reading, environment, failures),
                    VariableFoldersIn(reading, environment)));
            }
        }

        // A project that keeps one inventory file at its root rather than a folder per
        // environment. Its group_vars and host_vars, if any, sit beside it at the root.
        foreach (var name in RootInventoryNames)
        {
            if (reading.Find(name) is not { } file)
            {
                continue;
            }

            inventories.Add(new AnsibleInventory(
                name,
                name,
                GroupsOf(name, file, failures),
                VariableFoldersIn(reading, "")));
        }

        return inventories;
    }

    private static IReadOnlyList<AnsibleInventoryGroup> GroupsIn(Reading reading, string folder, List<TolerantYamlFailure> failures)
    {
        var groups = new List<AnsibleInventoryGroup>();
        foreach ((string relative, PluginFile file) in reading.FilesIn(folder).Where(entry => IsInventoryFile(entry.Relative)))
        {
            groups.AddRange(GroupsOf(relative, file, failures));
        }
        return groups;
    }

    private static IReadOnlyList<AnsibleInventoryGroup> GroupsOf(string relative, PluginFile file, List<TolerantYamlFailure> failures)
    {
        var result = PluginFileYaml.Read(file, relative);
        if (result is not TolerantYamlDocument { Root: YamlMappingNode mapping })
        {
            // An INI-format inventory is not YAML and will not parse. That is not the user's
            // mistake, so it is not recorded as a failure - it is read as the INI it is,
            // because a rule that answers "no inventory defines it" over groups the file
            // plainly defines is a well-formed wrong answer.
            return IniGroupsOf(file);
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

    private static IReadOnlyList<AnsibleVariableFolder> VariableFoldersIn(Reading reading, string owner)
    {
        var folders = new List<AnsibleVariableFolder>();
        foreach (var name in (string[])["group_vars", "host_vars"])
        {
            var path = Join(owner, name);
            if (reading.Listing.FolderExists(path))
            {
                folders.Add(new AnsibleVariableFolder(name, path, reading.Listing.FileCount(path)));
            }
        }
        return folders;
    }

    // ---- annotations and plumbing --------------------------------------------------------

    /// <summary>
    /// The files whose presence is all that is read of them. No file rule selects them, since their bytes
    /// are never read, so the listing says whether each is there.
    /// </summary>
    private static IReadOnlyList<string> ReadAnnotations(Reading reading)
    {
        var annotations = new List<string>();
        foreach (var name in AnnotationNames)
        {
            if (reading.Listing.FileExists(name))
            {
                annotations.Add(name);
            }
        }

        // collections/requirements.yml, the Galaxy convention.
        foreach (var name in (string[])["requirements.yml", "requirements.yaml"])
        {
            var path = "collections/" + name;
            if (reading.Listing.FileExists(path))
            {
                annotations.Add(path);
            }
        }

        return annotations;
    }

    private static void Record(TolerantYamlResult result, List<TolerantYamlFailure> failures)
    {
        if (result is TolerantYamlUnreadable unreadable &&
            !failures.Any(failure => string.Equals(failure.RelativePath, unreadable.Failure.RelativePath, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(unreadable.Failure);
        }
    }

    private static string? Scalar(YamlMappingNode? mapping, string key) =>
        mapping is null ? null : YamlAccess.Scalar(mapping, key);

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

    /// <summary>
    /// The classic INI inventory: <c>[group]</c> sections holding one host per line,
    /// <c>[group:children]</c> gathering other groups, <c>[group:vars]</c> holding
    /// configuration rather than membership. A range line (<c>web[01:50].example.com</c>)
    /// counts as one entry - the group's existence is what the rules need, the count is a
    /// summary.
    /// </summary>
    private static IReadOnlyList<AnsibleInventoryGroup> IniGroupsOf(PluginFile file)
    {
        var hostCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var childrenOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var order = new List<string>();
        string? section = null;
        var sectionIsChildren = false;

        using var text = PluginFileYaml.Text(file);
        while (text.ReadLine() is { } raw)
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
                if (hostCounts.TryAdd(name, 0))
                {
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

    /// <summary>The handed YAML files directly in <paramref name="folder"/>, by their exact extension, in ordinal order.</summary>
    private static IEnumerable<(string Relative, PluginFile File)> YamlFilesIn(Reading reading, string folder) =>
        reading.FilesIn(folder).Where(entry => IoPath.GetExtension(entry.Relative) is ".yml" or ".yaml");
}
