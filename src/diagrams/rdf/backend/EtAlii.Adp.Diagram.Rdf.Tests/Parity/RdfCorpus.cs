using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests.Parity;

/// <summary>
/// The parity corpus of the four RDF readings: every document each of the four transcripts records,
/// and the registrations beside it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every document is in all four transcripts</b> (rdf-disl-fbl task 1), so an entry one reading shows
/// under another is recorded before the readings are told apart.
/// </para>
/// <para>
/// <b>The order</b>: the test project's <c>Fixtures/</c>, the module's <c>examples/</c> and the shipped
/// copies under <c>src/examples/diagrams/rdf/</c>, each sorted by path. A body is a <c>.ttl</c> or an
/// <c>.nt</c> file; a registration is an <c>.adp</c> in the same folder whose <c>body:</c> names it.
/// </para>
/// </remarks>
internal static class RdfCorpus
{
    private const string TestProject = "EtAlii.Adp.Diagram.Rdf.Tests";

    /// <summary>The module's folder, <c>src/diagrams/rdf</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "rdf");

    /// <summary><c>src/</c>, which the corpus's paths are relative to.</summary>
    public static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The test project's <c>Parity</c> folder, where the transcripts are checked in.</summary>
    public static string ParityFolder => Path.Combine(ModuleFolder, "backend", TestProject, "Parity");

    /// <summary>The bodies, as paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    private static IReadOnlyList<string> Bodies()
    {
        var fixtures = Directory.GetFiles(Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures"));
        var examples = Directory.GetFiles(Path.Combine(ModuleFolder, "examples"), "*", SearchOption.AllDirectories);
        var shipped = Directory.GetFiles(Path.Combine(SourceFolder, "examples", "diagrams", "rdf"), "*", SearchOption.AllDirectories);
        return [.. Sorted(fixtures), .. Sorted(examples), .. Sorted(shipped)];

        static IEnumerable<string> Sorted(IEnumerable<string> paths) =>
            paths.Where(IsBody).Select(path => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/')).Order(StringComparer.Ordinal);
    }

    /// <summary>
    /// What a transcript of the reading with <paramref name="origin"/> records, in order: every body once
    /// for each registration of that origin beside it, or once bare when there is none, then each
    /// registration of that origin among the fixtures whose body does not exist.
    /// </summary>
    public static IReadOnlyList<(string Body, string? Registration)> Cases(string origin)
    {
        List<(string Body, string? Registration)> cases = [];
        foreach (var body in Bodies())
        {
            var registrations = RegistrationsOf(Path.Combine(SourceFolder, body)).Where(registration => OriginOf(registration) == origin).ToList();
            cases.AddRange(registrations.Count == 0 ? [(body, null)] : registrations.Select(registration => (body, (string?)registration)));
        }

        var fixtures = Path.Combine(ModuleFolder, "backend", TestProject, "Fixtures");
        foreach (var registration in Directory.GetFiles(fixtures, "*.adp").Order(StringComparer.Ordinal).Where(registration => OriginOf(registration) == origin))
        {
            var named = File.ReadLines(registration).Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("body: ", StringComparison.Ordinal))?["body: ".Length..];
            if (named is not null && !File.Exists(Path.Combine(fixtures, named)))
            {
                cases.Add((Path.GetRelativePath(SourceFolder, Path.Combine(fixtures, named)).Replace('\\', '/'), registration));
            }
        }

        return cases;
    }

    /// <summary>The registrations in the body's folder that name it in their <c>body:</c> header, by path.</summary>
    private static IReadOnlyList<string> RegistrationsOf(string body)
    {
        var name = Path.GetFileName(body);
        return
        [
            .. Directory.GetFiles(Path.GetDirectoryName(body)!, "*.adp")
                .Where(registration => File.ReadLines(registration).Any(line => line.Trim() == $"body: {name}"))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>The origin a registration states on its first line.</summary>
    private static string OriginOf(string registration) => File.ReadLines(registration).FirstOrDefault()?.Trim() ?? "";

    private static bool IsBody(string path) => Path.GetExtension(path) is ".ttl" or ".nt";
}
