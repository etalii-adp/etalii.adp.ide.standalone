using EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The bytes of a pipeline survive the tool (azure-devops-pipeline-disl-fbl Requirement 5.5, task 10): every document
/// of the corpus opened and saved without a change is written back as it was read, and every edit undone gives the
/// file back byte for byte, its redo gives the edit's bytes again, and an undo of a file changed since is refused.
/// </summary>
/// <remarks>
/// The undo puts back the library's inverse of the edit's splices (<see cref="RestorePipelineSpanCommand"/>), so
/// what is checked here is that inverse, for every command and every kind of element, on documents written in every
/// style the corpus holds: CRLF, tied endings, flush lists, comments, anchors and block scalars.
/// </remarks>
public class PipelineRoundTripTests
{
    private const int Sample = 2;

    public static TheoryData<string> Documents() => [.. PipelineReadings.Paths()];

    [Fact]
    public void TheCorpus_HoldsTheDocumentsWhoseEndingsAreTheTestSubject()
    {
        // Assert: the two documents whose line endings a rewrite would change, so the theories below read them.
        Assert.Contains(PipelineReadings.Paths(), path => path.EndsWith("/Fixtures/edge-crlf.yml", StringComparison.Ordinal));
        Assert.Contains(PipelineReadings.Paths(), path => path.EndsWith("/Fixtures/edge-tied-endings.yml", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ADocumentSavedUnchanged_IsWrittenAsItWasRead(string path)
    {
        // Arrange.
        using var folder = Copy(path, out var body);
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var store = new PipelineDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken);

        // Act: nothing asked for, then every element's display name as it already is.
        var nothing = entry.Document.Write([]);
        var same = entry.IsUsable
            ? entry.Model.Stages.Where(stage => stage.DisplayName.Length > 0).Select(stage => (stage.Id, stage.DisplayName))
                .Concat(entry.Model.Jobs.Where(job => job.DisplayName.Length > 0).Select(job => (job.Id, job.DisplayName)))

                // An id is the first element of it (finding A8), so a second stage of a name is not asked for.
                .DistinctBy(each => each.Id, StringComparer.Ordinal)
                .Select(each => PipelineDefinition.ElementOf(entry.Document, each.Id) is { } element && element.ValueOf("editable") is true
                    ? PipelineDefinition.Apply(entry.Document, FormCommit.Row(PipelineDefinition.Specification, element, "displayName", each.DisplayName, new DislEnv()))
                    : EditOutcome.Unchanged)
                .ToList()
            : [];
        var saved = await store.SaveAsync(ProjectRoot.From(folder.Path), body, entry, TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(nothing.IsUnchanged);
        Assert.All(same, outcome => Assert.True(outcome.IsUnchanged || !outcome.WasApplied, $"{path}: a display name set to itself was written."));
        Assert.Equal(original, entry.Document.Bytes);
        Assert.True(saved.Error.Length == 0 || !entry.IsUsable, saved.Error);
        Assert.Equal(original, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEditUndone_GivesTheBytesBack_AndItsRedoTheEditsBytes(string path)
    {
        // Arrange.
        using var folder = Copy(path, out var body);
        var original = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var model = (await new PipelineDocumentStore(new FileWriter()).GetOrLoadAsync(ProjectRoot.From(folder.Path), body, TestContext.Current.CancellationToken)).Model;
        List<string> failures = [];
        var applied = 0;

        // Act and assert.
        string[] ids =
        [
            .. model.Stages.Take(Sample).Select(stage => stage.Id),
            .. model.Jobs.Take(Sample).Select(job => job.Id),
            .. model.Jobs.SelectMany(job => job.Steps).Take(Sample).Select(step => step.Id),
        ];
        var other = model.Stages.Skip(1).FirstOrDefault()?.Name ?? "";
        foreach (var id in ids)
        {
            await Check(new RenamePipelineElementCommand(ProjectRoot.From(folder.Path), body, id, "Round trip"));
            await Check(new SetPipelineElementEnabledCommand(ProjectRoot.From(folder.Path), body, id, false));
            await Check(new SetPipelineDependenciesCommand(ProjectRoot.From(folder.Path), body, id, [], Declared: false));
            await Check(new SetPipelineDependenciesCommand(ProjectRoot.From(folder.Path), body, id, [], Declared: true));
            await Check(new SetPipelineDependenciesCommand(ProjectRoot.From(folder.Path), body, id, [other], Declared: true));
            await Check(new AddPipelineElementCommand(ProjectRoot.From(folder.Path), body, PipelineAddKind.Job, id));
            await Check(new AddPipelineElementCommand(ProjectRoot.From(folder.Path), body, PipelineAddKind.DeploymentJob, id));
            await Check(new AddPipelineElementCommand(ProjectRoot.From(folder.Path), body, PipelineAddKind.Step, id));
            await Check(new RemovePipelineElementCommand(ProjectRoot.From(folder.Path), body, id));
            await Check(new MovePipelineStepCommand(ProjectRoot.From(folder.Path), body, id, 0));
            await Check(new MovePipelineStepCommand(ProjectRoot.From(folder.Path), body, id, int.MaxValue));
        }

        await Check(new AddPipelineElementCommand(ProjectRoot.From(folder.Path), body, PipelineAddKind.Stage, ""));
        Assert.True(failures.Count == 0, $"{path}: {failures.Count} of {applied} edits\n{string.Join("\n", failures)}");
        if (model.Stages.Any(stage => stage is { IsImplicit: false, IsFromTemplate: false })) Assert.True(applied > 0, $"{path}: no edit applied, so nothing was undone.");
        return;

        async Task Check(IPipelineEditCommand command)
        {
            var cancellationToken = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(body, original, cancellationToken);
            var dispatcher = new PipelineTestDispatcher(new PipelineDocumentStore(new FileWriter()));
            var done = await dispatcher.DispatchAsync(command, cancellationToken);
            if (done is not { IsSuccess: true, Inverse: not null }) return;

            applied++;
            var edited = await File.ReadAllBytesAsync(body, cancellationToken);
            var undone = await dispatcher.DispatchAsync(done.Inverse, cancellationToken);
            var afterUndo = await File.ReadAllBytesAsync(body, cancellationToken);
            var redone = undone.Inverse is { } redo ? await dispatcher.DispatchAsync(redo, cancellationToken) : null;
            var afterRedo = await File.ReadAllBytesAsync(body, cancellationToken);
            if (!undone.IsSuccess || !afterUndo.AsSpan().SequenceEqual(original))
            {
                failures.Add($"  {command}: the undo {(undone.IsSuccess ? "did not give the bytes back" : $"was refused: {undone.Error}")}");
            }
            else if (redone is not { IsSuccess: true } || !afterRedo.AsSpan().SequenceEqual(edited))
            {
                failures.Add($"  {command}: the redo {(redone is { IsSuccess: false } ? $"was refused: {redone.Error}" : "did not write the edit's bytes")}");
            }
        }
    }

    [Fact]
    public async Task AnUndoOfAFileChangedSince_IsRefused_AndWritesNothing()
    {
        // Arrange.
        using var folder = Copy("diagrams/azure-devops-pipeline/backend/EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests/Fixtures/multi-stage.yml", out var body);
        var store = new PipelineDocumentStore(new FileWriter());
        var dispatcher = new PipelineTestDispatcher(store);
        var removed = await dispatcher.DispatchAsync(new RemovePipelineElementCommand(ProjectRoot.From(folder.Path), body, "Build/Compile/step-1"), TestContext.Current.CancellationToken);
        Assert.True(removed.IsSuccess, removed.Error);
        await File.AppendAllTextAsync(body, "# changed by someone else\r\n", TestContext.Current.CancellationToken);
        await store.ReloadAsync(body, TestContext.Current.CancellationToken);
        var changed = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);

        // Act.
        var undone = await dispatcher.DispatchAsync(removed.Inverse!, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(SplicedFile.DriftUndo, undone.Error);
        Assert.Equal(changed, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    /// <summary>A copy of the folder the corpus document <paramref name="path"/> is read in, and the copy's path of it.</summary>
    private static TestFolder Copy(string path, out string body)
    {
        var document = PipelineCorpus.Documents().Single(candidate => candidate.Relative == path);
        var folder = TestFolder.Create("EtAlii.Adp.PipelineRoundTrip");
        PipelineCorpus.CopyFolder(IoPath.Combine(PipelineCorpus.SourceFolder, document.Root), folder.Path);
        body = IoPath.Combine(folder.Path, document.InFolder);
        return folder;
    }
}
