using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;

/// <summary>
/// The commands run from the DISL definition and planned by the persistence plugin do what the hand-written ones did
/// (azure-devops-pipeline-disl-fbl task 10): for every readable document of the parity corpus, every command on a
/// sample of its stages, jobs and steps gives the same refusal as <see cref="HandWrittenPipelineEdits"/>, or a file
/// that reads as the same pipeline.
/// </summary>
/// <remarks>
/// <para>
/// <b>The same pipeline, not the same bytes</b>: Q1 rules that a value is written in its own span and style, a new
/// key in the binding's order, a new element after the last entry of its list at the file's own indentation, and a
/// removal without the blank lines after it, so the bytes differ where the hand-written writer wrote otherwise. What
/// is compared is every stage, job and step as the parser reads the file back: its id, name, display name, what it
/// waits for, whether it runs, and a step's kind, script and hook. A gate is left out, because a new stage beside a
/// gated one is no longer gated (finding B10).
/// </para>
/// <para>
/// <b>One refusal is new</b>: a rename or a disable of a key that arrives through a merge, which the writer wrote as
/// the element's own key and the plugin refuses (finding B13, Q1). It is expected here by its sentence.
/// </para>
/// <para>
/// <b>One refusal is gone</b>: where the hand-written writer broke the file, as it did writing a key into a step under
/// <c>${{ if }}</c> (finding B4), the store refused to save it, and the plugin writes the key in the entry's own
/// mapping. Such an edit is counted and listed, not compared: it is a question for the user.
/// </para>
/// </remarks>
public class PipelineDerivedOperationsTests
{
    private const int Sample = 3;
    private const string ThroughAMerge = "The value is reached through a merge key, so it is read-only.";

    public static TheoryData<string> Documents() => [.. PipelineReadings.Paths()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryCommand_RefusesOrWritesWhatTheHandWrittenOneDid(string path)
    {
        // Arrange.
        var document = PipelineCorpus.Documents().Single(candidate => candidate.Relative == path);
        using var folder = TestFolder.Create("EtAlii.Adp.PipelineDerivedOperations");
        PipelineCorpus.CopyFolder(Path.Combine(PipelineCorpus.SourceFolder, document.Root), folder.Path);
        var workspace = folder.Path;
        var body = Path.Combine(folder.Path, document.InFolder);
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var entry = await new PipelineDocumentStore(new FileWriter()).GetOrLoadAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken);
        if (!entry.IsUsable) return;

        var model = entry.Model;
        List<string> compared = [];
        List<string> mismatches = [];
        List<string> broken = [];

        // Act and assert.
        var stages = model.Stages.Take(Sample).ToList();
        var jobs = model.Jobs.Take(Sample).ToList();
        var steps = model.Jobs.SelectMany(job => job.Steps).Take(Sample).ToList();
        string[] ids = [.. stages.Select(stage => stage.Id), .. jobs.Select(job => job.Id), .. steps.Select(step => step.Id), "parity:unknown"];
        var otherStage = model.Stages.Skip(1).FirstOrDefault()?.Name ?? "";
        foreach (var id in ids)
        {
            await Compare($"rename {id}", root => new RenamePipelineElementCommand(ProjectRoot.From(root), body, id, "Parity name"), (text, read) => HandWrittenPipelineEdits.Rename(text, read, id, "Parity name"));
            await Compare($"disable {id}", root => new SetPipelineElementEnabledCommand(ProjectRoot.From(root), body, id, false), (text, read) => HandWrittenPipelineEdits.SetEnabled(text, read, id, false));
            await Compare($"enable {id}", root => new SetPipelineElementEnabledCommand(ProjectRoot.From(root), body, id, true), (text, read) => HandWrittenPipelineEdits.SetEnabled(text, read, id, true));
            foreach ((string name, IReadOnlyList<string> names, bool declared) in new (string, IReadOnlyList<string>, bool)[]
                     {
                         ("the default order", [], false), ("nothing", [], true), ("nobody", ["nobody"], true), (otherStage, [otherStage], true),
                         ("its own name", [model.Stages.FirstOrDefault(stage => stage.Id == id)?.Name ?? model.Jobs.FirstOrDefault(job => job.Id == id)?.Name ?? id], true),
                     })
            {
                // A name nobody wrote is not one a gesture asks for: the row offers only the names there are.
                if (names.Any(each => each.Length == 0)) continue;

                await Compare($"dependencies of {id} on {name}", root => new SetPipelineDependenciesCommand(ProjectRoot.From(root), body, id, names, declared), (text, read) => HandWrittenPipelineEdits.SetDependencies(text, read, id, names, declared));
            }

            foreach (var kind in new[] { PipelineAddKind.Job, PipelineAddKind.DeploymentJob, PipelineAddKind.Step })
            {
                await Compare($"add {kind} to {id}", root => new AddPipelineElementCommand(ProjectRoot.From(root), body, kind, id), (text, read) => HandWrittenPipelineEdits.Add(text, read, kind, id));
            }

            await Compare($"remove {id}", root => new RemovePipelineElementCommand(ProjectRoot.From(root), body, id), (text, read) => HandWrittenPipelineEdits.Remove(text, read, id));
            await Compare($"move {id} up", root => new MovePipelineStepCommand(ProjectRoot.From(root), body, id, IndexOf(model, id) - 1), (text, read) => HandWrittenPipelineEdits.MoveStep(text, read, id, IndexOf(model, id) - 1));
            await Compare($"move {id} down", root => new MovePipelineStepCommand(ProjectRoot.From(root), body, id, IndexOf(model, id) + 1), (text, read) => HandWrittenPipelineEdits.MoveStep(text, read, id, IndexOf(model, id) + 1));
        }

        await Compare("add a stage", root => new AddPipelineElementCommand(ProjectRoot.From(root), body, PipelineAddKind.Stage, ""), (text, read) => HandWrittenPipelineEdits.Add(text, read, PipelineAddKind.Stage, ""));
        await Compare("add a stage named Build", root => new AddPipelineElementCommand(ProjectRoot.From(root), body, PipelineAddKind.Stage, "", "Build"), (text, read) => HandWrittenPipelineEdits.Add(text, read, PipelineAddKind.Stage, "", "Build"));

        Assert.True(mismatches.Count == 0, $"{path}: {mismatches.Count} of {compared.Count} differ\n{string.Join("\n", mismatches)}");
        Assert.True(broken.Count == 0 || path.EndsWith("/findings/gates-nested.yml", StringComparison.Ordinal), $"{path}: the hand-written writer broke the file in {string.Join(", ", broken)}");
        return;

        async Task Compare(string edit, Func<string, IPipelineEditCommand> command, Func<LineDocument, PipelineModel, string?> handWritten)
        {
            compared.Add(edit);

            // The hand-written edit, on the text as it is, with the templates followed as the store follows them.
            var text = LineDocument.Parse(Encoding.UTF8.GetString(original));
            var refusal = handWritten(text, PipelineParser.Parse(text, new PipelineTemplates(ProjectRoot.From(workspace)), body));
            var written = refusal is null ? PipelineParser.Outcome(LineDocument.Parse(text.Text), new PipelineTemplates(ProjectRoot.From(workspace)), body) : null;
            var expected = refusal is not null ? $"refused: {refusal}"
                : written!.IsUsable ? Pipeline(written.Model)
                : $"a file that does not parse: {written.Unreadable}";

            // The command, through a store of its own, on the file as it was.
            await File.WriteAllBytesAsync(body, original, TestContext.Current.CancellationToken);
            var store = new PipelineDocumentStore(new FileWriter());
            var result = await TestHistory.Dispatcher(s => { s.AddAzurePipeline(); s.AddSingleton<IPipelineDocumentStore>(store); }).DispatchAsync(command(workspace), TestContext.Current.CancellationToken);
            var actual = result.IsSuccess ? Pipeline((await new PipelineDocumentStore(new FileWriter()).GetOrLoadAsync(ProjectRoot.From(workspace), body, TestContext.Current.CancellationToken)).Model) : $"refused: {result.Error}";

            if (actual == $"refused: {ThroughAMerge}" && expected != actual) return;
            if (written is { IsUsable: false })
            {
                broken.Add(edit);
                return;
            }

            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                mismatches.Add($"{edit}\n  hand-written {expected.Replace("\n", "\n               ", StringComparison.Ordinal)}\n  definition   {actual.Replace("\n", "\n               ", StringComparison.Ordinal)}");
            }
        }
    }

    /// <summary>Where a step is among its job's steps; -1 for anything else.</summary>
    private static int IndexOf(PipelineModel model, string id) =>
        model.Jobs.Select(job => job.Steps.ToList().FindIndex(step => step.Id == id)).FirstOrDefault(index => index >= 0, -1);

    /// <summary>The pipeline as it is compared: every stage, job and step, one line each.</summary>
    private static string Pipeline(PipelineModel model)
    {
        var lines = new StringBuilder();
        foreach (var stage in model.Stages)
        {
            lines.Append($"stage {stage.Id} {stage.Name} '{stage.DisplayName}' {Waits(stage.DependsOnDeclared, stage.DependsOn)} {Runs(stage.Execution)}\n");
            foreach (var job in stage.Jobs)
            {
                lines.Append($"  job {job.Id} {job.Name} '{job.DisplayName}' {(job.IsDeployment ? $"deployment to {job.Environment}" : "job")} {Waits(job.DependsOnDeclared, job.DependsOn)} {Runs(job.Execution)}\n");
                foreach (var step in job.Steps)
                {
                    lines.Append($"    step {step.Id} {step.Kind} '{step.Identifier}' '{step.DisplayName}' {step.Hook} {Runs(step.Execution)}\n");
                }
            }
        }

        return lines.ToString();

        static string Waits(bool declared, IReadOnlyList<string> names) => declared ? $"waits for [{string.Join(", ", names)}]" : "in the default order";

        static string Runs(PipelineExecution execution) => execution.IsDisabled ? "disabled" : "enabled";
    }
}
