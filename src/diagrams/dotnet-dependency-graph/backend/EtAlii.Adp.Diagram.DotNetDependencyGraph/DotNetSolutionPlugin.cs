using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Serilog;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// The FBL §11 persistence plugin of the binding <c>dotnet-dependency-graph.fbl#solution</c>
/// (<c>net.etalii.adp.dotnet.solution</c>): it reads one solution, the projects it names and the packages they
/// reference, with the module's own readers and <see cref="DependencyGraph"/>, which run as they always have.
/// </summary>
/// <remarks>
/// <para>
/// <b>The body is the solution; the rest is the subject's</b> (Q1). <see cref="Read"/> is handed the solution's
/// bytes and decodes them as a file read today is decoded; the project files, the versions files above them and the
/// package cache are reached through the <see cref="DotNetSolutionSubject"/> the module made for this body, because
/// FBL has no subject that is one file with the files it leads to (the companion's gap 1).
/// </para>
/// <para>
/// <b>An element is delivered with its natural id</b>, which the definition's <c>natural</c> id strategy prefixes:
/// a project by its path relative to the solution, a package by its id, so the model's ids are today's
/// <c>project:</c> and <c>package:</c> ids. A reference keeps the id it has today, <c>depends:</c> and both ends,
/// stored, and names its ends by their natural ids whether or not its target is in the graph (Q5): a package
/// referenced in another casing is a relation whose end names nothing, as it is today.
/// </para>
/// <para>
/// <b>Every element has its source</b> (Q6): a project its entry in the solution, a reference its declaration in
/// the project file, a package its first declaration in reading order, each with its file and line, the file
/// relative to the solution's folder and null for the body. See <see cref="EntrySpan"/> for the span.
/// </para>
/// <para>
/// <b>No finding is delivered and the body is never unreadable</b> (Q2, Requirement 3.8): a solution that will not
/// parse is an empty graph that carries its reason, and every failure stays on <see cref="Graph"/> and in the log,
/// as today.
/// </para>
/// </remarks>
public sealed class DotNetSolutionPlugin(DotNetSolutionSubject subject) : IPersistencePlugin
{
    /// <summary>The plugin's id, as the binding's <c>reader.plugin</c> names it.</summary>
    public const string PluginId = "net.etalii.adp.dotnet.solution";

    private const string ProjectType = "Project";
    private const string PackageType = "Package";
    private const string ProjectReferenceType = "ProjectReference";
    private const string PackageReferenceType = "PackageReference";

    private static readonly ILogger _logger = Log.ForContext<DotNetSolutionPlugin>();

    private readonly DotNetSolutionSubject _subject = subject ?? throw new ArgumentNullException(nameof(subject));
    private readonly DependencyGraph _graphs = new();

    /// <inheritdoc />
    public string Id => PluginId;

    /// <summary>The graph of the last read, with the failures that travel with it; empty before the first.</summary>
    public DependencyGraphModel Graph { get; private set; } = DependencyGraphModel.Empty;

    /// <inheritdoc />
    public PluginReadResult Read(PluginReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var body = request.Files.FirstOrDefault(file => file.RelativePath.Length == 0)
            ?? throw new ArgumentException("The request holds no body.", nameof(request));

        var solution = _subject.Solution(Decoded(body.Bytes));
        var readings = new Dictionary<string, ProjectReading>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            readings[project.AbsolutePath] = _subject.Project(project.AbsolutePath);
        }

        var graph = _graphs.Derive(solution, readings);

        // Descriptions last, and separately: they come from outside the workspace entirely - the
        // machine's package cache - so a graph is complete without them and merely less
        // informative. A package the cache has never seen keeps a null description, which the
        // property grid renders as an explicit absence.
        Graph = graph with
        {
            Packages = [.. graph.Packages.Select(package => package with { Description = _subject.Description(package.PackageId, package.Versions) })],
        };

        _logger.Debug("Read {Solution} through the plugin", _subject.SolutionPath);
        return new PluginReadResult(Elements(Graph, Sources(body.Bytes, solution, readings)), [], false);
    }

    /// <summary>
    /// The elements of <paramref name="graph"/> as <see cref="Read"/> gives them, without sources: for a graph no
    /// file was read for, such as a hand-built one, and for the builder parity test.
    /// </summary>
    public static IReadOnlyList<FblElement> Elements(DependencyGraphModel graph) => Elements(graph, new Dictionary<string, ElementSource>(StringComparer.Ordinal));

    /// <summary>Refuses: the binding is read-only, so <c>PluginBody.Plan</c> refuses before it would ask (FBL 11.4).</summary>
    public PluginPlanResult Plan(PluginPlanRequest request) => new PluginPlanResult.Refused(DotNetDefinition.Refusal);

    /// <summary>Never asked: the binding has a <c>template.text</c>.</summary>
    public byte[] Template(PluginTemplateRequest request) =>
        throw new NotSupportedException("The binding's template.text is the new body; the plugin has no template of its own.");

    /// <summary>Nothing yet: the session still watches the files through the store (the specification's task 7 moves it here).</summary>
    public IReadOnlyList<string> Watch(PluginReadResult last) => [];

    private static IReadOnlyList<FblElement> Elements(DependencyGraphModel graph, Dictionary<string, ElementSource> sources)
    {
        var elements = new List<FblElement>();
        foreach (var project in graph.Projects)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = project.Name,
                ["path"] = project.RelativePath,
                ["targetFrameworks"] = project.TargetFrameworks,
            };
            if (project.DotNetVersion is { } version) attributes["dotNetVersion"] = version;
            elements.Add(Element(project.Id, ProjectType, attributes, relation: false, null, null, sources));
        }

        foreach (var package in graph.Packages)
        {
            var attributes = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["name"] = package.PackageId,
                ["versions"] = package.Versions,
                ["hasVersionConflict"] = package.HasVersionConflict,
                ["dependentProjectCount"] = package.DependentProjectCount,
                ["isAmbient"] = package.IsAmbient,
            };
            if (package.Description is { } description) attributes["description"] = description;
            elements.Add(Element(package.Id, PackageType, attributes, relation: false, null, null, sources));
        }

        foreach (var edge in graph.Edges)
        {
            var type = edge.Kind == DependsOnKind.Project ? ProjectReferenceType : PackageReferenceType;
            elements.Add(Element(edge.Id, type, new Dictionary<string, object?>(), relation: true, NaturalId(edge.FromElementId), NaturalId(edge.ToElementId), sources));
        }

        return elements;
    }

    /// <summary>One element: a node by its natural id, a relation by its stored one, with its source where the reading has one.</summary>
    private static FblElement Element(string id, string type, Dictionary<string, object?> attributes, bool relation, string? source, string? target, Dictionary<string, ElementSource> sources)
    {
        var at = sources.GetValueOrDefault(id);
        return new FblElement(relation ? id : NaturalId(id), true, type, type, relation, attributes, null, null, source, target, at.Entry.Span, at.Entry.Line)
        {
            File = at.File,
        };
    }

    /// <summary>
    /// The id an element is read with: a node's model id without the prefix the definition gives its type, which the
    /// builder puts back. An id with neither prefix is its own.
    /// </summary>
    private static string NaturalId(string modelId) =>
        modelId.StartsWith(DependencyGraph.ProjectPrefix, StringComparison.Ordinal) ? modelId[DependencyGraph.ProjectPrefix.Length..]
        : modelId.StartsWith(DependencyGraph.PackagePrefix, StringComparison.Ordinal) ? modelId[DependencyGraph.PackagePrefix.Length..]
        : modelId;

    /// <summary>
    /// The source of every element of one reading, by model id (Q6): a project at its entry in the solution, a
    /// reference at its declaration, the first where one project declares it twice, and a package at its first
    /// declaration in reading order, its id compared without regard to case as the graph groups it.
    /// </summary>
    private static Dictionary<string, ElementSource> Sources(byte[] body, SolutionReading solution, Dictionary<string, ProjectReading> readings)
    {
        var sources = new Dictionary<string, ElementSource>(StringComparer.Ordinal);
        var packages = new Dictionary<string, ElementSource>(StringComparer.OrdinalIgnoreCase);
        var idByProjectPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in solution.Projects)
        {
            var id = DependencyGraph.IdOfProject(project.RelativePath);
            idByProjectPath.TryAdd(project.AbsolutePath, id);
            sources.TryAdd(id, new ElementSource(null, new EntrySource(project.Line, EntrySpan.StartTag(body, project.Line, project.Column))));
        }

        foreach (var project in solution.Projects)
        {
            if (!readings.TryGetValue(project.AbsolutePath, out var reading)) continue;
            var from = idByProjectPath[project.AbsolutePath];
            foreach (var reference in reading.ProjectReferences)
            {
                if (idByProjectPath.TryGetValue(reference.AbsolutePath, out var to))
                {
                    sources.TryAdd(DependencyGraph.IdOfEdge(from, to), new ElementSource(project.RelativePath, reference.Source));
                }
            }

            foreach (var reference in reading.PackageReferences)
            {
                var declared = new ElementSource(project.RelativePath, reference.Source);
                sources.TryAdd(DependencyGraph.IdOfEdge(from, DependencyGraph.IdOfPackage(reference.PackageId)), declared);
                packages.TryAdd(reference.PackageId, declared);
            }
        }

        foreach ((string packageId, ElementSource declared) in packages)
        {
            sources.TryAdd(DependencyGraph.IdOfPackage(packageId), declared);
        }

        return sources;
    }

    /// <summary>The body's text, decoded as <c>SharedDocumentReader.ReadAllText</c> decodes a file: UTF-8 unless a byte order mark says otherwise.</summary>
    private static string Decoded(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false));
        return reader.ReadToEnd();
    }

    /// <summary>Where an element is: its file relative to the solution's folder, null for the body, and its entry there.</summary>
    private readonly record struct ElementSource(string? File, EntrySource Entry);
}
