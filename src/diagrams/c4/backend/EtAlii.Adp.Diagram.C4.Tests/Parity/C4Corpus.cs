using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using Microsoft.Extensions.DependencyInjection;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.C4.Tests.Parity;

/// <summary>
/// The documents the C4 parity transcript is written over (c4-disl-fbl Requirement 1.2), in the order
/// they are recorded.
/// </summary>
/// <remarks>
/// <para>
/// <b>Four sources</b>: the test project's <c>Fixtures/*.dsl</c>, then <c>Fixtures/findings/*.dsl</c>
/// (one document per finding of the specification marked <i>read</i>, kept out of the module's other
/// sweeps, which name their fixtures one by one), the module's <c>examples/*/architecture/*.dsl</c>
/// and the shipped copies under <c>src/examples/diagrams/c4/</c>, and last a new document of each of
/// the six C4 types, as <see cref="ServiceCollectionAddC4Extension"/>'s factory writes it.
/// </para>
/// <para>
/// <b>What travels with a document</b>: a fixture takes the <c>&lt;base&gt;.layout.json</c> beside
/// it, when there is one; an example takes every file of its folder, so its registrations and its
/// sidecar are read as the host reads them.
/// </para>
/// </remarks>
internal static class C4Corpus
{
    public const string TestProject = "EtAlii.Adp.Diagram.C4.Tests";

    /// <summary>The base name every new document of the corpus is created with.</summary>
    private const string NewBaseName = "parity";

    /// <summary>The module's folder, <c>src/diagrams/c4</c>.</summary>
    public static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "c4");

    /// <summary><c>src/</c>, which every recorded path is relative to.</summary>
    private static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The six C4 types that draw a view, each with the view kind it binds, in the order discovery lists them.</summary>
    public static IReadOnlyList<(ToolOrigin Origin, C4ViewKind Kind)> Types { get; } =
    [
        (Diagram.SystemContext.Origin, C4ViewKind.SystemContext),
        (Diagram.Container.Origin, C4ViewKind.Container),
        (Diagram.Component.Origin, C4ViewKind.Component),
        (Diagram.SystemLandscape.Origin, C4ViewKind.SystemLandscape),
        (Diagram.Dynamic.Origin, C4ViewKind.Dynamic),
        (Diagram.Deployment.Origin, C4ViewKind.Deployment),
    ];

    /// <summary>The type that draws a view of <paramref name="kind"/>.</summary>
    public static ToolOrigin OriginOf(C4ViewKind kind) => Types.First(type => type.Kind == kind).Origin;

    /// <summary>
    /// One document: its recorded name, its bytes, and the files that travel with it, each as its
    /// name in the document's folder and its bytes.
    /// </summary>
    public sealed record Document(string Relative, byte[] Bytes, IReadOnlyList<(string Name, byte[] Bytes)> Companions);

    /// <summary>Every document of the corpus, in the order it is recorded.</summary>
    public static async Task<IReadOnlyList<Document>> DocumentsAsync(CancellationToken cancellationToken)
    {
        var fixtures = Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures");
        var documents = new List<Document>();
        foreach (var body in Directory.GetFiles(fixtures, "*.dsl").Order(StringComparer.Ordinal).Concat(Directory.GetFiles(Path.Combine(fixtures, "findings"), "*.dsl").Order(StringComparer.Ordinal)))
        {
            var sidecar = C4LayoutSidecar.PathFor(body);
            List<(string, byte[])> companions = File.Exists(sidecar) ? [(Path.GetFileName(sidecar), await File.ReadAllBytesAsync(sidecar, cancellationToken))] : [];
            documents.Add(new Document(RepositoryPaths.RelativeToSource(body), await File.ReadAllBytesAsync(body, cancellationToken), companions));
        }

        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples"))
            .Concat(Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "c4")))
            .SelectMany(project => Directory.GetFiles(project, "*.dsl", SearchOption.AllDirectories));
        foreach (var body in examples.Order(StringComparer.Ordinal))
        {
            var folder = Path.GetDirectoryName(body)!;
            var companions = new List<(string, byte[])>();
            foreach (var file in Directory.GetFiles(folder).Order(StringComparer.Ordinal).Where(file => !string.Equals(file, body, StringComparison.Ordinal)))
            {
                companions.Add((Path.GetFileName(file), await File.ReadAllBytesAsync(file, cancellationToken)));
            }

            documents.Add(new Document(RepositoryPaths.RelativeToSource(body), await File.ReadAllBytesAsync(body, cancellationToken), companions));
        }

        await using var services = new ServiceCollection().AddC4().BuildServiceProvider();
        var factories = services.GetServices<IDiagramDocumentFactory>().ToList();
        foreach ((ToolOrigin origin, _) in Types)
        {
            var text = factories.Single(factory => factory.Origin == origin).CreateEmptyDocument(NewBaseName);
            documents.Add(new Document($"new {origin.Key}/{NewBaseName}{Diagram.DocumentExtension}", TranscriptText.Encode(text), []));
        }

        return documents;
    }

    /// <summary>
    /// One document of the corpus as the module reads it: its recorded name, the file name its body
    /// was read from, its workspace, and for each view the ids of what that view draws.
    /// </summary>
    public sealed record Reading(string Name, string File, C4Workspace Workspace, IReadOnlyList<(C4View View, IReadOnlyList<string> Drawn)> Views);

    /// <summary>
    /// Every document of the corpus, read as the transcript reads it: in a temporary folder of its own
    /// with the files that travel with it, through the module's store, and drawn by its mapper.
    /// </summary>
    public static async Task<IReadOnlyList<Reading>> ReadingsAsync(CancellationToken cancellationToken)
    {
        var readings = new List<Reading>();
        await using var services = new ServiceCollection().AddCommands().AddHierarchyCommandHandlers().AddC4().BuildServiceProvider();
        var store = services.GetRequiredService<IC4DocumentStore>();
        var mapper = services.GetRequiredService<C4ElementMapper>();
        foreach (var document in await DocumentsAsync(cancellationToken))
        {
            using var scratch = TestFolder.Create("EtAlii.Adp.C4Corpus");
            var folder = scratch.Path;
            var body = Path.Combine(folder, Path.GetFileName(document.Relative));
            await File.WriteAllBytesAsync(body, document.Bytes, cancellationToken);
            foreach ((string name, byte[] bytes) in document.Companions)
            {
                await File.WriteAllBytesAsync(Path.Combine(folder, name), bytes, cancellationToken);
            }

            var workspace = (await store.GetOrLoadAsync(body, cancellationToken)).Model;
            List<(C4View, IReadOnlyList<string>)> views =
            [
                .. workspace.Views.Select(view => (view, (IReadOnlyList<string>)[.. mapper.Visible(workspace, view, DiagramViewport.Unbounded, body).Select(element => element.Id)])),
            ];
            readings.Add(new Reading(document.Relative, Path.GetFileName(body), workspace, views));
            store.Forget(body);
        }

        return readings;
    }

    /// <summary>An id no document of the corpus gives anything.</summary>
    public const string Unknown = "parity:unknown";

    /// <summary>
    /// Every target the derived menus and rows are compared on: under each view of a document, with the
    /// origin of that view's kind, every element and relationship of the model, everything the view
    /// draws, the empty canvas and an unknown id; then with no view, under each of the six origins, the
    /// model's targets, the empty canvas and the unknown id.
    /// </summary>
    public static IEnumerable<(string Document, string File, C4View? View, ToolOrigin Origin, string Target, C4Workspace Workspace)> Targets(IReadOnlyList<Reading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        foreach (var reading in readings)
        {
            var workspace = reading.Workspace;
            List<string> model =
            [
                .. workspace.Elements.Select(element => element.Id)
                    .Concat(workspace.Relationships.Select(relationship => relationship.Id))
                    .Distinct(StringComparer.Ordinal),
            ];
            foreach ((C4View view, IReadOnlyList<string> drawn) in reading.Views)
            {
                foreach (var target in model.Concat(drawn).Append("").Append(Unknown).Distinct(StringComparer.Ordinal))
                {
                    yield return (reading.Name, reading.File, view, OriginOf(view.Kind), target, workspace);
                }
            }

            foreach ((ToolOrigin origin, _) in Types)
            {
                foreach (var target in model.Append("").Append(Unknown))
                {
                    yield return (reading.Name, reading.File, null, origin, target, workspace);
                }
            }
        }
    }
}
