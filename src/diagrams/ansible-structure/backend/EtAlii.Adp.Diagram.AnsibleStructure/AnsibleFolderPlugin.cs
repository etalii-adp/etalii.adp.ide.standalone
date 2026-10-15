using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>ansible-structure.fbl#ansible</c>
/// (<c>net.etalii.adp.ansible.folder</c>): it reads an Ansible project folder with
/// <see cref="AnsibleProjectReader"/> and <see cref="AnsibleGraph"/>, the module's own reading, so the
/// DISL model holds exactly the nodes and relations the canvas, the rows and the findings have always read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a plugin</b> (FBL 11.5, finding B1): a role is a folder, a playbook is a file whose whole top level
/// has a shape, a task file is drawn only when something includes it, and a target resolves by a path
/// relative to the file that names it. None of that is a selector or a slot.
/// </para>
/// <para>
/// <b>What it is handed and what it learns</b>: the selected files, by relative path and bytes, and from
/// <see cref="AnsibleFolderListing"/>, the seam of Requirement 4.3, what FBL cannot hand a plugin. It holds
/// that listing, which is a function of the folder and not a reading, and keeps nothing it read.
/// </para>
/// <para>
/// <b>What it delivers</b>: the graph's nodes in the graph's order, then its relations in the graph's
/// order (finding B6), each with its file and line and with the graph's id, which the definition's
/// <c>derived</c> ids compute again. A relation whose target is missing or an expression has no target
/// (finding B7). A file that does not parse is one <c>std.unparseable</c> at its file and the parser's line.
/// </para>
/// <para>
/// <b>It reads; it does not plan.</b> The binding is read-only, so the host never asks it for a plan or a
/// template, and both throw.
/// </para>
/// </remarks>
public sealed class AnsibleFolderPlugin : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    private const string PluginId = "net.etalii.adp.ansible.folder";

    /// <summary>
    /// What the reading depends on beyond the files the rules select, relative to the folder: the folders
    /// whose existence and counts it reads, and the files whose presence it records.
    /// </summary>
    private static readonly string[] Watched = ["ansible.cfg", "collections", "group_vars", "host_vars", "inventories", "roles"];

    private readonly AnsibleFolderListing _listing;
    private readonly AnsibleProjectReader _reader = new();

    /// <summary>The plugin for the folder <paramref name="listing"/> lists.</summary>
    public AnsibleFolderPlugin(AnsibleFolderListing listing)
    {
        ArgumentNullException.ThrowIfNull(listing);
        _listing = listing;
    }

    /// <inheritdoc />
    public string Id => PluginId;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Result(_reader.Read(request.Files, _listing));
    }

    /// <summary>What <see cref="Read"/> delivers for a project already read: its elements and a finding per file that did not parse.</summary>
    public static PluginReadResult Result(AnsibleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var findings = project.Failures
            .Select(failure => new Finding(FindingCodes.Unparseable, FindingSeverity.Error, failure.Message, new SourceLocation(failure.RelativePath, (int)failure.Line, 1, 0)))
            .ToList();
        return new PluginReadResult(Elements(project, AnsibleGraph.Derive(project)), findings, false);
    }

    /// <summary>
    /// The elements of <paramref name="graph"/>, the graph of <paramref name="project"/>: one per node in the
    /// graph's order, then one per relation in the graph's order, each carrying the attributes the
    /// definition declares for it.
    /// </summary>
    private static IReadOnlyList<FblElement> Elements(AnsibleProject project, AnsibleGraph graph)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(graph);

        var elements = new List<FblElement>(graph.Nodes.Count + graph.Edges.Count);
        foreach (var node in graph.Nodes)
        {
            (string type, Dictionary<string, object?> attributes, int line) = node.Kind switch
            {
                AnsibleNodeKind.Playbook => ("Playbook", Playbook(project, node), 0),
                AnsibleNodeKind.Play => Play(project, node),
                AnsibleNodeKind.Role => ("Role", Role(project, node), 0),
                AnsibleNodeKind.TaskFile => ("TaskFile", Identity(node), 0),
                AnsibleNodeKind.Inventory => ("Inventory", Inventory(project, node), 0),
                AnsibleNodeKind.VariableFolder => ("VariableFolder", VariableFolder(project, node), 0),
                _ => throw new InvalidOperationException($"A node of kind {node.Kind} has no type in the definition."),
            };
            elements.Add(new FblElement(node.Id, false, type, type, false, attributes, null, null, null, null, default, line) { File = node.RelativePath });
        }

        foreach (var edge in graph.Edges)
        {
            var type = edge.Kind.ToString();
            var target = edge is { Resolution: AnsibleTargetResolution.Resolved, TargetId.Length: > 0 } ? edge.TargetId : null;
            elements.Add(new FblElement(edge.Id, false, type, type, true, Relation(edge), null, null, edge.SourceId, target, default, (int)edge.Directive.Line)
            {
                File = edge.Directive.DeclaredIn,
            });
        }

        return elements;
    }

    /// <inheritdoc />
    public PluginPlanResult Plan(PluginPlanRequest request) =>
        throw new InvalidOperationException("An Ansible structure diagram is read-only, so its plugin is never asked to plan a change.");

    /// <inheritdoc />
    public byte[] Template(PluginTemplateRequest request) =>
        throw new InvalidOperationException("An Ansible structure diagram is a folder that is already there, so its plugin is never asked for a template.");

    /// <inheritdoc />
    public IReadOnlyList<string> Watch(PluginReadResult last) => Watched;

    private static Dictionary<string, object?> Playbook(AnsibleProject project, AnsibleNode node)
    {
        var attributes = Identity(node);
        attributes["annotations"] = Strings(project.Annotations);
        if (PlaybookOf(project, node) is { } playbook)
        {
            attributes["plays"] = playbook.Plays.Select(play => (object?)new CelMap
            {
                ["ordinal"] = (long)play.Index,
                ["name"] = play.Name,
                ["hosts"] = play.Hosts,
                ["line"] = (long)play.Line,
            }).ToList();

            // The playbook stands for its play when it holds exactly one, and carries its hosts.
            attributes["hosts"] = playbook.Plays.Count == 1 ? playbook.Plays[0].Hosts : "";
        }

        return attributes;
    }

    private static (string Type, Dictionary<string, object?> Attributes, int Line) Play(AnsibleProject project, AnsibleNode node)
    {
        var attributes = Identity(node);
        attributes["annotations"] = Strings(project.Annotations);
        attributes["playIndex"] = (long)node.PlayIndex;

        // A play's id is its playbook's path, '#' and its index among the playbook's plays.
        var line = 0;
        var hash = node.Id.LastIndexOf('#');
        if (hash >= 0 && int.TryParse(node.Id[(hash + 1)..], out var index)
            && PlaybookOf(project, node)?.Plays.FirstOrDefault(candidate => candidate.Index == index) is { } play)
        {
            attributes["ordinal"] = (long)play.Index;
            attributes["hosts"] = play.Hosts;
            line = (int)play.Line;
        }

        return ("Play", attributes, line);
    }

    private static Dictionary<string, object?> Role(AnsibleProject project, AnsibleNode node)
    {
        var attributes = Identity(node);
        if (project.Role(node.Name) is { Contents: var contents })
        {
            attributes["contents"] = new CelMap
            {
                ["taskFiles"] = (long)contents.TaskFiles,
                ["handlers"] = (long)contents.Handlers,
                ["templates"] = (long)contents.Templates,
                ["files"] = (long)contents.Files,
                ["defaults"] = (long)contents.Defaults,
                ["vars"] = (long)contents.Vars,
                ["hasMeta"] = contents.HasMeta,
                ["hasLibrary"] = contents.HasLibrary,
            };
        }

        return attributes;
    }

    private static Dictionary<string, object?> Inventory(AnsibleProject project, AnsibleNode node)
    {
        var attributes = Identity(node);
        var inventory = project.Inventories.FirstOrDefault(candidate =>
            string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));
        if (inventory is not null)
        {
            attributes["groups"] = inventory.Groups
                .Select(group => (object?)new CelMap { ["name"] = group.Name, ["hostCount"] = (long)group.HostCount })
                .ToList();
            attributes["variableFolders"] = inventory.VariableFolders
                .Select(folder => (object?)new CelMap { ["name"] = folder.Name, ["path"] = folder.RelativePath, ["fileCount"] = (long)folder.FileCount })
                .ToList();
        }

        return attributes;
    }

    private static Dictionary<string, object?> VariableFolder(AnsibleProject project, AnsibleNode node)
    {
        var attributes = Identity(node);
        var folder = project.Inventories
            .SelectMany(inventory => inventory.VariableFolders)
            .FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));
        if (folder is not null)
        {
            attributes["fileCount"] = (long)folder.FileCount;
        }

        return attributes;
    }

    private static Dictionary<string, object?> Relation(AnsibleEdge edge) =>
        new(StringComparer.Ordinal)
        {
            ["directive"] = Directive(edge.Directive.Kind),
            ["targetAsWritten"] = edge.Directive.Target,
            ["condition"] = edge.Directive.Condition,
            ["declaredIn"] = edge.Directive.DeclaredIn,
            ["declaredAtLine"] = (long)edge.Directive.Line,
            ["resolution"] = edge.Resolution switch
            {
                AnsibleTargetResolution.Resolved => "resolved",
                AnsibleTargetResolution.Missing => "missing",
                _ => "expression",
            },
        };

    /// <summary>The definition's <c>Directive</c> value of <paramref name="kind"/>.</summary>
    private static string Directive(AnsibleDirectiveKind kind) => kind switch
    {
        AnsibleDirectiveKind.Roles => "roles",
        AnsibleDirectiveKind.ImportRole => "importRole",
        AnsibleDirectiveKind.IncludeRole => "includeRole",
        AnsibleDirectiveKind.ImportPlaybook => "importPlaybook",
        AnsibleDirectiveKind.ImportTasks => "importTasks",
        AnsibleDirectiveKind.IncludeTasks => "includeTasks",
        AnsibleDirectiveKind.Dependency => "dependencies",
        _ => throw new InvalidOperationException($"The directive {kind} has no value in the definition."),
    };

    private static AnsiblePlaybook? PlaybookOf(AnsibleProject project, AnsibleNode node) =>
        project.Playbooks.FirstOrDefault(candidate => string.Equals(candidate.RelativePath, node.RelativePath, StringComparison.Ordinal));

    private static Dictionary<string, object?> Identity(AnsibleNode node) =>
        new(StringComparer.Ordinal) { ["name"] = node.Name, ["path"] = node.RelativePath };

    private static List<object?> Strings(IEnumerable<string> values) => [.. values];
}
