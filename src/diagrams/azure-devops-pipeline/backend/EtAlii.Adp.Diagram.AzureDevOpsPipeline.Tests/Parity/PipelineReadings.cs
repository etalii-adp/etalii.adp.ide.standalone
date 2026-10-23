using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// A document of the parity corpus as the providers read it: opened in a copy of the folder it was taken
/// from, so the templates it names are followed, with the targets the transcript records menus and rows
/// for and the entry both readable and read-only.
/// </summary>
internal sealed class PipelineReadings : IDisposable
{
    private const string Unknown = "parity:unknown";

    private readonly TestFolder _folder;

    private PipelineReadings(TestFolder folder, DocumentEntry<PipelineBody, PipelineModel> entry)
    {
        _folder = folder;
        var drawn = new PipelineElementMapper(PipelineMetrics.Default)
            .Visible(entry.Model, DiagramViewport.Unbounded, entry.Model.Stages.SelectMany(stage => stage.Jobs.Select(job => job.Id).Prepend(stage.Id)).ToHashSet(StringComparer.Ordinal))
            .Select(element => element.Id);
        Targets =
        [
            .. entry.Model.Stages
                .SelectMany(stage => stage.Jobs.SelectMany(job => job.Steps.Select(step => step.Id).Prepend(job.Id)).Prepend(stage.Id))
                .Concat(drawn)
                .Distinct(StringComparer.Ordinal),
            "",
            Unknown,
        ];
        Entries = [entry, entry with { Unreadable = "parity: read-only" }];
    }

    /// <summary>Every stage, job and step, every element the mapper draws with everything open, the empty id and an unknown id.</summary>
    public IReadOnlyList<string> Targets { get; }

    /// <summary>The entry as read, and the same entry as one that could not be read.</summary>
    public IReadOnlyList<DocumentEntry<PipelineBody, PipelineModel>> Entries { get; }

    /// <summary>The corpus's documents, by their path under <c>src</c>.</summary>
    public static IEnumerable<string> Paths() => PipelineCorpus.Documents().Select(document => document.Relative);

    /// <summary>Opens the corpus document at <paramref name="path"/> in a copy of its folder.</summary>
    public static async Task<PipelineReadings> OpenAsync(string path, CancellationToken cancellationToken)
    {
        var document = PipelineCorpus.Documents().Single(candidate => candidate.Relative == path);
        var folder = TestFolder.Create("EtAlii.Adp.PipelineReadings");
        PipelineCorpus.CopyFolder(Path.Combine(PipelineCorpus.SourceFolder, document.Root), folder.Path);
        var entry = await new PipelineDocumentStore(new FileWriter()).GetOrLoadAsync(ProjectRoot.From(folder.Path), folder.Combine(document.InFolder), cancellationToken);
        return new PipelineReadings(folder, entry);
    }

    public void Dispose() => _folder.Dispose();
}
