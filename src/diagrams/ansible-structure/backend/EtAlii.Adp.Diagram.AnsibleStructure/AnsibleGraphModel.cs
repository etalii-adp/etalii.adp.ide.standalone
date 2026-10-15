using EtAlii.Adp.Specification.Cel;
using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// Today's reading of an Ansible folder as the definition's model: one node per node of
/// <see cref="AnsibleGraph"/> and one relation per edge, in the graph's order and with the graph's ids,
/// each carrying the attributes the definition declares for it.
/// </summary>
/// <remarks>
/// <para>
/// <b>A stand-in for the binding, until the folder is read through it</b> (ansible-structure-disl-fbl,
/// task 7, which deletes this class): derivation changes in one step and reading in another, so a
/// difference in the transcript names one of them. Until then <see cref="AnsibleProjectReader"/> reads
/// the folder and this hands what it read to the definition, as the plugin will.
/// </para>
/// <para>
/// <b>Every element keeps the graph's id.</b> The definition derives the same ids (<c>persistence.ids</c>),
/// and nothing here asks the library to compute them, so a stored position and a selection name what
/// they named before.
/// </para>
/// <para>
/// <b>A relation finds its ends by id</b>, the first node of that id in model order, as a model read from
/// a file finds them; a target that is missing or an expression is no end at all, so the relation has
/// none (an <c>optional</c> target in the definition).
/// </para>
/// </remarks>
internal static class AnsibleGraphModel
{
    public static DislDiagram Build(DislSpecification specification, AnsibleProject project, AnsibleGraph graph)
    {
        ArgumentNullException.ThrowIfNull(specification);
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(graph);

        var diagram = new DislDiagram(specification);
        var nodes = new Dictionary<string, DislElement>(StringComparer.Ordinal);

        foreach (var node in graph.Nodes)
        {
            (string type, Dictionary<string, object?> attributes) = node.Kind switch
            {
                AnsibleNodeKind.Playbook => ("Playbook", Playbook(project, node)),
                AnsibleNodeKind.Play => ("Play", Play(project, node)),
                AnsibleNodeKind.Role => ("Role", Role(project, node)),
                AnsibleNodeKind.TaskFile => ("TaskFile", Identity(node)),
                AnsibleNodeKind.Inventory => ("Inventory", Inventory(project, node)),
                AnsibleNodeKind.VariableFolder => ("VariableFolder", VariableFolder(project, node)),
                _ => throw new InvalidOperationException($"A node of kind {node.Kind} has no type in the definition."),
            };
            nodes.TryAdd(node.Id, diagram.AddNode(type, node.Id, attributes));
        }

        foreach (var edge in graph.Edges)
        {
            diagram.AddRelation(
                edge.Kind.ToString(),
                edge.Id,
                nodes.GetValueOrDefault(edge.SourceId),
                nodes.GetValueOrDefault(edge.TargetId),
                Relation(edge));
        }

        return diagram;
    }

    /// <summary>
    /// What the reader could not read, as the reader's findings the definition reports: one
    /// <c>std.unparseable</c> per file that did not parse, at that file and the parser's line, its reason the
    /// parser's own words.
    /// </summary>
    public static IReadOnlyList<DislReaderFinding> ReaderFindings(AnsibleProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return [.. project.Failures.Select(failure => DislReaderFinding.NotParsed(failure.Message, failure.Line == 0 ? null : (int)failure.Line, failure.RelativePath))];
    }

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

    private static Dictionary<string, object?> Play(AnsibleProject project, AnsibleNode node)
    {
        var attributes = Identity(node);
        attributes["annotations"] = Strings(project.Annotations);
        attributes["playIndex"] = (long)node.PlayIndex;

        // A play's id is its playbook's path, '#' and its index among the playbook's plays.
        var hash = node.Id.LastIndexOf('#');
        if (hash >= 0 && int.TryParse(node.Id[(hash + 1)..], out var index)
            && PlaybookOf(project, node)?.Plays.FirstOrDefault(candidate => candidate.Index == index) is { } play)
        {
            attributes["ordinal"] = (long)play.Index;
            attributes["hosts"] = play.Hosts;
        }

        return attributes;
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
