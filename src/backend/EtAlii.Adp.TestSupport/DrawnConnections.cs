using EtAlii.Adp.Common;
using EtAlii.Adp.Common.Wire;
using EtAlii.Adp.Context;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Hierarchy;
using Microsoft.Extensions.DependencyInjection;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// The element types one module's projection emits, named by reference to the constants the
/// emitting mapper itself declares - never as string literals in a test.
/// </summary>
/// <param name="Connections">The types the canvas draws as lines: every element of these must resolve.</param>
/// <param name="Elements">Every other type the projection emits over the module's shipped examples.</param>
/// <param name="NoConnectionsBecause">
/// Required exactly when <paramref name="Connections"/> is empty: the reason in words, so "this module
/// draws no connection elements" is a statement someone made rather than an empty set nobody noticed.
/// </param>
public sealed record DrawnTypes(
    IReadOnlyCollection<string> Connections,
    IReadOnlyCollection<string> Elements,
    string? NoConnectionsBecause = null);

/// <summary>One open diagram, as a module's <see cref="DrawnModule.ExpandViews"/> hook sees it.</summary>
/// <param name="WatchId">The connection the session was opened for - the key a module's per-connection view state uses.</param>
/// <param name="BodyPath">The diagram's body.</param>
/// <param name="Baseline">What the freshly opened diagram drew.</param>
public sealed record DrawnView(ShortGuid WatchId, string BodyPath, IReadOnlyList<DiagramElement> Baseline);

/// <summary>One module, as <see cref="DrawnConnections"/> checks it.</summary>
/// <param name="ExamplesFolder">The module's folder under <c>src/examples/diagrams</c>.</param>
/// <param name="Types">Its emitted types, by reference to its mapper's constants.</param>
/// <param name="ExpandViews">
/// For a module whose canvas draws more after a view change than on open - a pipeline's job-level arrows
/// exist only inside an expanded stage - the hook that opens every such view before the diagram is read
/// again. Omitted, the baseline is all there is. Declared per module so that a module which draws on
/// expansion says so where it can be seen.
/// </param>
public sealed record DrawnModule(string ExamplesFolder, DrawnTypes Types, Action<IServiceProvider, DrawnView>? ExpandViews = null);

/// <summary>
/// The backend twin of the client's <c>expectLibrarySelection</c>: <b>every connection a module's canvas
/// draws resolves to a selection.</b>
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists</b> (centralized-selection task 26). The library pushes a pressed connection's own id,
/// and the highlight follows the backend's answer, so a connection is selectable on a canvas only if a
/// context resolver resolves the id. Every client test mocks the backend, so no client guard can see a
/// resolver that refuses: <c>azure-pipeline</c> refused every arrow and nothing noticed.
/// </para>
/// <para>
/// <b>What it drives, all through core's own seams</b>: each diagram in the module's shipped examples is
/// routed by <see cref="DiagramFileRouter"/>, opened by its registered <see cref="IDiagramSessionFactory"/>,
/// and read (after the module's <see cref="DrawnModule.ExpandViews"/>, if it has one). Each connection is
/// then offered to every registered <see cref="IContextSourceResolver"/> that claims element ids, the first
/// acceptance winning - exactly as <c>ContextSelectionResolver</c> does - inside the diagram's file level.
/// <b>From both files a diagram can be opened through</b>: its <c>.adp</c> registration, and its body when
/// the body routes on its own, because a canvas selection nests under whichever the tab was opened at.
/// </para>
/// <para>
/// <b>Four assertions, collected and reported together, each naming module, example and id</b>: (i) every
/// emitted element has a type the test named - which keeps a new edge type from slipping past and a "no
/// connections" statement honest; (ii) every named type is emitted somewhere in the examples, so a named
/// type is one the corpus actually shows; (iii) every connection resolves; (iv) a module naming no
/// connection types gives its reason. <b>No exemption list</b>: a connection resolves whether or not the
/// client declares it selectable, so selectability stays a client declaration alone.
/// </para>
/// </remarks>
public static class DrawnConnections
{
    /// <summary>
    /// Opens every diagram in the module's examples through <paramref name="services"/> and returns every
    /// problem found, each naming the module, the example and the id. Empty means all four hold.
    /// </summary>
    /// <param name="services">The real composed host's provider - every module registered - so a resolver
    /// answering "not mine" to a foreign id is exercised as well.</param>
    /// <param name="module">The module to check.</param>
    public static async Task<IReadOnlyList<string>> ProblemsAsync(IServiceProvider services, DrawnModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);

        var name = module.ExamplesFolder;
        var types = module.Types;
        var problems = new List<string>();
        if (types.Connections.Count == 0 && string.IsNullOrWhiteSpace(types.NoConnectionsBecause))
        {
            problems.Add($"{name}: names no connection types and gives no reason - say why in NoConnectionsBecause, or name them.");
        }
        if (types.Connections.Count > 0 && !string.IsNullOrWhiteSpace(types.NoConnectionsBecause))
        {
            problems.Add($"{name}: names connection types and also says it has none.");
        }
        foreach (var both in types.Connections.Intersect(types.Elements, StringComparer.Ordinal))
        {
            problems.Add($"{name}: names {both} as both a connection and an element type.");
        }

        var workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-drawn-connections-" + Guid.NewGuid().ToString("N"));
        try
        {
            // A copy, so opening a diagram can never write into the shipped examples.
            var source = IoPath.Combine(ExamplesRoot(), "diagrams", name);
            if (!Directory.Exists(source))
            {
                return [.. problems, $"{name}: no examples folder at {source}."];
            }
            CopyTree(source, workspace);

            var router = services.GetRequiredService<DiagramFileRouter>();
            var factories = services.GetServices<IDiagramSessionFactory>().ToList();
            var resolvers = services.GetServices<IContextSourceResolver>().ToList();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var named = new HashSet<string>(types.Connections.Concat(types.Elements), StringComparer.Ordinal);
            var connections = new HashSet<string>(types.Connections, StringComparer.Ordinal);

            var diagrams = DiagramsIn(workspace, router, factories);
            if (diagrams.Count == 0)
            {
                return [.. problems, $"{name}: opened no diagram under {source}, so nothing was checked."];
            }

            foreach (var (example, routed, factory) in diagrams)
            {
                var bodyPath = routed.BodyPath ?? routed.RegistrationPath!;
                var watchId = ShortGuid.NewShortGuid();
                await using var session = factory.Open(watchId, workspace, bodyPath, routed.RegistrationPath);
                var elements = AddedBy(session.Baseline());
                if (module.ExpandViews is { } expand)
                {
                    expand(services, new DrawnView(watchId, bodyPath, elements));
                    elements = AddedBy(session.Baseline());
                }

                var parents = ParentsOf(routed, router, workspace);
                foreach (var element in elements)
                {
                    seen.Add(element.Type);
                    if (!named.Contains(element.Type))
                    {
                        problems.Add($"{name}: {example} emits {element.Id} of type {element.Type}, which the test names as neither a connection nor an element type.");
                        continue;
                    }
                    if (!connections.Contains(element.Type))
                    {
                        continue;
                    }

                    foreach (var parent in parents)
                    {
                        var answer = await ResolveAsync(resolvers, workspace, parent, routed.Definition.Origin, element.Id);
                        if (answer is not null)
                        {
                            problems.Add($"{name}: {example} connection {element.Id} ({element.Type}) does not resolve when opened through {IoPath.GetFileName(parent)} - {answer}.");
                        }
                    }
                }
            }

            foreach (var unseen in named.Where(type => !seen.Contains(type)).Order(StringComparer.Ordinal))
            {
                problems.Add($"{name}: names {unseen}, which no example emitted - name only what the examples show; the union check fails the day it appears.");
            }
        }
        finally
        {
            TestFolder.TryDelete(workspace);
        }

        return problems;
    }

    private static List<DiagramElement> AddedBy(IReadOnlyList<DiagramDelta> deltas) =>
        [.. deltas.OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements)];

    /// <summary>
    /// The files a tab can have opened this diagram at: its registration, and its body when the body
    /// routes to the same type on its own. A folder subject's body is not a file a tab opens.
    /// </summary>
    private static List<string> ParentsOf(DiagramRouted routed, DiagramFileRouter router, string workspace)
    {
        var parents = new List<string>();
        if (routed.RegistrationPath is not null)
        {
            parents.Add(routed.RegistrationPath);
        }
        if (routed.BodyPath is { } body &&
            File.Exists(body) &&
            !parents.Contains(body, StringComparer.OrdinalIgnoreCase) &&
            router.Route(body, workspace) is DiagramRouted { Definition.Origin: var origin } &&
            origin == routed.Definition.Origin)
        {
            parents.Add(body);
        }
        return parents;
    }

    /// <summary>Null when the id resolves; otherwise what every claimant answered, in words.</summary>
    private static async Task<string?> ResolveAsync(
        IReadOnlyList<IContextSourceResolver> resolvers,
        string workspace,
        string parentPath,
        DiagramOrigin origin,
        string elementId)
    {
        var id = new ContextSource { ElementId = new ElementId { Value = elementId } };
        var claimants = resolvers.Where(candidate => candidate.CanResolve(id)).ToList();
        if (claimants.Count == 0)
        {
            return "no registered resolver claims element ids";
        }

        // Every claimant is asked and the first acceptance wins, exactly as ContextSelectionResolver
        // does: CanResolve answers only "is this an element id", which every diagram type says yes to.
        var refusals = new List<string>();
        foreach (var claimant in claimants)
        {
            // The file level a canvas press is resolved inside: the file the tab was opened at.
            var fileLevel = new ContextResolvedLevel(
                ContextSelectionSource.Explorer,
                new ContextSource(),
                [IoPath.GetFileName(parentPath)],
                ContextScope.Hierarchy,
                new ContextTarget(ContextScope.Hierarchy, parentPath, IsContainer: false, SourceId: default, workspace, ShortGuid.NewShortGuid(), Origin: origin),
                new ContextLevelDetail(),
                claimant);
            try
            {
                var resolution = await claimant.ResolveAsync(
                    ShortGuid.NewShortGuid(),
                    workspace,
                    ContextSelectionSource.DiagramCanvas,
                    id,
                    [],
                    fileLevel,
                    CancellationToken.None);
                if (resolution is ResolvedContextLevel)
                {
                    return null;
                }
                refusals.Add($"{claimant.GetType().Name}: {resolution.GetType().Name}");
            }
            catch (Exception exception)
            {
                refusals.Add($"{claimant.GetType().Name} threw {exception.GetType().Name}: {exception.Message}");
            }
        }
        return "every claimant refused (" + string.Join("; ", refusals) + ")";
    }

    /// <summary>
    /// Every diagram the module opens under <paramref name="workspace"/>: each registration, and each body
    /// with no registration beside it that routes on its own.
    /// </summary>
    private static List<(string Example, DiagramRouted Routed, IDiagramSessionFactory Factory)> DiagramsIn(
        string workspace,
        DiagramFileRouter router,
        IReadOnlyList<IDiagramSessionFactory> factories)
    {
        var found = new List<(string, DiagramRouted, IDiagramSessionFactory)>();
        var bodies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Registrations first, so a body they open is not visited a second time on its own.
        foreach (var file in files.Where(DiagramFilePair.IsRegistrationFile).Concat(files.Where(file => !DiagramFilePair.IsRegistrationFile(file))))
        {
            if (bodies.Contains(file) || router.Route(file, workspace) is not DiagramRouted routed)
            {
                continue;
            }
            var factory = factories.FirstOrDefault(candidate => candidate.Origin == routed.Definition.Origin);
            if (factory is null)
            {
                continue;
            }
            if (routed.BodyPath is not null)
            {
                bodies.Add(routed.BodyPath);
            }
            found.Add((IoPath.GetRelativePath(workspace, file).Replace('\\', '/'), routed, factory));
        }
        return found;
    }

    /// <summary>The repository's src/examples folder, found by walking up from the test binary.</summary>
    public static string ExamplesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "examples");
            if (Directory.Exists(IoPath.Combine(candidate, "diagrams")))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException($"No src/examples/diagrams folder above {AppContext.BaseDirectory}.");
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var target = IoPath.Combine(to, IoPath.GetRelativePath(from, file));
            Directory.CreateDirectory(IoPath.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
