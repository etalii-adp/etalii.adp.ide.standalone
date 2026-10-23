using System.Collections.ObjectModel;
using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// What the derived-against-hand-written tests compare over (databricks-disl-fbl task 6): every document of the parity
/// corpus, each opened as every reading the transcript records, and per reading every element id the module draws for
/// the reading draws, the empty id, a placement, two connect gestures and an unknown id.
/// </summary>
/// <remarks>
/// <b>The readings</b> are the transcript's: per tool type, with no registration, and for a job or a pipeline with a
/// registration whose <c>resource:</c> header names each resource of its kind and one that names nothing. The ids are
/// those the reading draws, as the transcript records them: the hand-written menus also answered an id the reading does
/// not draw - an edge of a job the registration does not select, the pipeline of a reading that selects none - which no
/// gesture on the canvas can send, and the derived ones answer nothing there.
/// </remarks>
internal sealed class DatabricksParityCorpus : IDisposable
{
    private const string MissingResource = "parity-missing";

    private static readonly ToolOrigin[] _origins =
    [
        ServiceCollectionAddDatabricksExtension.BundleOrigin,
        ServiceCollectionAddDatabricksExtension.JobOrigin,
        ServiceCollectionAddDatabricksExtension.PipelineOrigin,
    ];

    private readonly TestFolder _testFolder = TestFolder.Create("EtAlii.Adp.DatabricksParity");
    private readonly string _folder;

    public DatabricksParityCorpus() => _folder = _testFolder.Path;

    /// <summary>The three tool types.</summary>
    public static IReadOnlyList<ToolOrigin> Origins => _origins;

    /// <summary>Every document of the corpus, by a name a failure can show: the fixtures, then the lakehouse example and its copy.</summary>
    public static TheoryData<string> Documents()
    {
        var documents = new TheoryData<string>();
        foreach (var path in Paths())
        {
            documents.Add(path.Name);
        }

        return documents;
    }

    /// <summary>The entry the module's store would hold for the document <paramref name="name"/>.</summary>
    public static async Task<DocumentEntry<LineDocument, DatabricksModel>> EntryAsync(string name, CancellationToken cancellationToken)
    {
        var text = await File.ReadAllTextAsync(Paths().Single(path => path.Name == name).Path, cancellationToken);
        var document = LineDocument.Parse(text);
        return new DocumentEntry<LineDocument, DatabricksModel>(document, DatabricksModel.Outcome(document));
    }

    /// <summary>The ways the document of <paramref name="model"/> is opened: a tool type and the registration it is opened through, if any.</summary>
    public async Task<IReadOnlyList<(string Label, ToolOrigin Origin, string? Registration)>> OpeningsAsync(DatabricksModel model, CancellationToken cancellationToken)
    {
        var openings = new List<(string, ToolOrigin, string?)>();
        foreach (var origin in _origins)
        {
            openings.Add(($"{origin.Key} | no registration", origin, null));
            IEnumerable<string> keys = origin.Type switch
            {
                "job" => model.Jobs.Select(job => job.Key),
                "pipeline" => model.Pipelines.Select(pipeline => pipeline.Key).Where(key => key.Length > 0),
                _ => [],
            };
            if (origin.Type == "bundle")
            {
                continue;
            }

            foreach (var key in keys.Append(MissingResource).Distinct(StringComparer.Ordinal))
            {
                var registration = IoPath.Combine(_folder, $"{origin.Type}-{openings.Count}.adp");
                await File.WriteAllTextAsync(registration, $"{origin.Key}\nbody: parity.yml\nresource: {key}\n", cancellationToken);
                openings.Add(($"{origin.Key} | resource: {key}", origin, registration));
            }
        }

        return openings;
    }

    /// <summary>
    /// Every id the reading of the tool type <paramref name="origin"/>, opened through <paramref name="registration"/>,
    /// draws - the resource the registration's <c>resource:</c> header names, or the file's first, as the session picks
    /// it - then the empty id, a placement, two connect gestures and an unknown id: the targets the transcript records.
    /// </summary>
    public static IReadOnlyList<string> Targets(DatabricksModel model, ToolOrigin origin, string? registration)
    {
        var mapper = new DatabricksElementMapper();
        var key = DatabricksHeaders.ReadResourceKey(registration);
        var none = ReadOnlyDictionary<string, RegistrationPosition>.Empty;
        IReadOnlyList<DiagramElement> elements = origin.Type switch
        {
            "job" => Pick(model.Jobs, candidate => candidate.Key, key) is { } job ? mapper.JobElements(job, none) : [],
            "pipeline" => Pick(model.Pipelines, candidate => candidate.Key, key) is { } pipeline ? mapper.PipelineElements(pipeline, none) : [],
            _ => mapper.BundleElements(model.Bundle, none),
        };
        var boxes = elements.Where(element => !element.Type.EndsWith("+edge", StringComparison.Ordinal)).Select(element => element.Id).ToList();
        var placement = GestureIds.Placement(120, 80);
        return
        [
            .. elements.Select(element => element.Id)
                .Append("")
                .Append(placement)
                .Append(GestureIds.Relation(boxes.ElementAtOrDefault(0) ?? "task:a", boxes.ElementAtOrDefault(1) ?? "task:b"))
                .Append(GestureIds.Relation(placement, boxes.ElementAtOrDefault(0) ?? "task:a"))
                .Append("parity:unknown")
                .Distinct(StringComparer.Ordinal),
        ];

        static T? Pick<T>(IReadOnlyList<T> candidates, Func<T, string> keyOf, string? named)
            where T : class =>
            named is { Length: > 0 } ? candidates.FirstOrDefault(candidate => keyOf(candidate) == named) : candidates.FirstOrDefault();
    }

    /// <summary>A target as the host resolves one: in the document, for the tool type, through the registration.</summary>
    public static ContextTarget Target(string elementId, ToolOrigin origin, string? registration) =>
        ContextTarget.ForElement(IoPath.Combine(IoPath.GetTempPath(), "parity.yml"), elementId, origin: origin) with { RegistrationPath = registration };

    public void Dispose() => _testFolder.Dispose();

    private static IEnumerable<(string Name, string Path)> Paths()
    {
        var fixtures = IoPath.Combine(AppContext.BaseDirectory, "Fixtures");
        foreach (var path in Directory.EnumerateFiles(fixtures).Where(IsBody).Order(StringComparer.Ordinal))
        {
            yield return (IoPath.GetFileName(path), path);
        }

        foreach ((string prefix, string folder) in (IEnumerable<(string, string)>)
            [
                ("lakehouse", RepositoryPaths.Under("src", "diagrams", "databricks", "examples", "lakehouse")),
                ("examples/lakehouse", RepositoryPaths.Under("src", "examples", "diagrams", "databricks", "lakehouse")),
            ])
        {
            foreach (var path in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(IsBody).Order(StringComparer.Ordinal))
            {
                yield return ($"{prefix}/{IoPath.GetRelativePath(folder, path).Replace('\\', '/')}", path);
            }
        }
    }

    private static bool IsBody(string path) => IoPath.GetExtension(path) is ".yml" or ".yaml" or ".json";
}
