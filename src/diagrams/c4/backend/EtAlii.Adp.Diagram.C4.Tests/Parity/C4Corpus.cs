using EtAlii.Adp.Documents;
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
        foreach (var body in Sorted(Directory.GetFiles(fixtures, "*.dsl")).Concat(Sorted(Directory.GetFiles(Path.Combine(fixtures, "findings"), "*.dsl"))))
        {
            var sidecar = C4LayoutSidecar.PathFor(body);
            List<(string, byte[])> companions = File.Exists(sidecar) ? [(Path.GetFileName(sidecar), await File.ReadAllBytesAsync(sidecar, cancellationToken))] : [];
            documents.Add(new Document(RelativeToSource(body), await File.ReadAllBytesAsync(body, cancellationToken), companions));
        }

        var examples = Directory.GetDirectories(Path.Combine(ModuleFolder, "examples"))
            .Concat(Directory.GetDirectories(Path.Combine(SourceFolder, "examples", "diagrams", "c4")))
            .SelectMany(project => Directory.GetFiles(project, "*.dsl", SearchOption.AllDirectories));
        foreach (var body in Sorted(examples))
        {
            var folder = Path.GetDirectoryName(body)!;
            var companions = new List<(string, byte[])>();
            foreach (var file in Sorted(Directory.GetFiles(folder)).Where(file => !string.Equals(file, body, StringComparison.Ordinal)))
            {
                companions.Add((Path.GetFileName(file), await File.ReadAllBytesAsync(file, cancellationToken)));
            }

            documents.Add(new Document(RelativeToSource(body), await File.ReadAllBytesAsync(body, cancellationToken), companions));
        }

        await using var services = new ServiceCollection().AddC4().BuildServiceProvider();
        var factories = services.GetServices<IDiagramDocumentFactory>().ToList();
        foreach ((ToolOrigin origin, _) in Types)
        {
            var text = factories.Single(factory => factory.Origin == origin).CreateEmptyDocument(NewBaseName);
            documents.Add(new Document($"new {origin.Key}/{NewBaseName}{Diagram.DocumentExtension}", TranscriptText.Encode(text), []));
        }

        return documents;

        static IEnumerable<string> Sorted(IEnumerable<string> paths) => paths.Order(StringComparer.Ordinal);
    }

    /// <summary>A path as the transcript names it: relative to <c>src/</c>, with forward slashes.</summary>
    private static string RelativeToSource(string path) => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/');
}
