using EtAlii.Adp.Diagram.Databricks.Tests.Parity;
using EtAlii.Adp.Files;
using EtAlii.Adp.History;
using EtAlii.Adp.Specification.Fbl.Yaml;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The round trip over the whole parity corpus (databricks-disl-fbl task 12, Requirements 6.5 and 6.8): a document
/// saved without a change, or given a value it already holds, keeps its bytes, and every edit the commands make on it,
/// undone once, gives its bytes back. The corpus holds the line-ending fixtures and the document without a final newline.
/// </summary>
public sealed class DatabricksRoundTripTests
{
    public static TheoryData<string> Documents() => DatabricksParityCorpus.Documents();

    [Fact]
    public void TheCorpus_HoldsTheLineEndingFixtures_AndTheOneWithoutAFinalNewline()
    {
        // Act.
        var names = Documents().Select(row => row.Data).ToList();

        // Assert.
        Assert.Contains("crlf-line-endings.yml", names);
        Assert.Contains("lf-line-endings.yml", names);
        Assert.Contains("tied-line-endings.yml", names);
        Assert.Contains("no-trailing-newline.yml", names);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task ADocumentSavedWithoutAChange_KeepsItsBytes(string name)
    {
        // Arrange.
        using var editing = Copy(name);
        var original = await editing.BytesAsync();
        var store = new DatabricksDocumentStore(new FileWriter());
        var entry = await store.GetOrLoadAsync(editing.Body, TestContext.Current.CancellationToken);

        // Act.
        await store.SaveAsync(editing.Body, entry.Document, TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task AValueSetToWhatItAlreadyIs_GivesNoSplice(string name)
    {
        // Arrange.
        using var editing = Copy(name);
        var original = await editing.BytesAsync();
        var entry = await DatabricksParityCorpus.EntryAsync(name, TestContext.Current.CancellationToken);
        if (!entry.IsUsable) return;

        var model = entry.Model;

        // Act.
        var results = new List<CommandResult>();
        // A value the YAML core schema types, such as 2024, is not the text it is set to: its set writes the text quoted,
        // as the writers did, so only a value that reads back as itself is set to itself here.
        if (model.Bundle.Name.Length > 0 && YamlScalars.IsPlainSafe(model.Bundle.Name, false))
        {
            results.Add(await editing.RunAsync(body => new SetDatabricksBundleNameCommand(body, model.Bundle.Name)));
        }

        foreach (var pipeline in model.Pipelines.Where(pipeline => pipeline.Name.Length > 0 && YamlScalars.IsPlainSafe(pipeline.Name, false)))
        {
            results.Add(await editing.RunAsync(body => new SetDatabricksPipelineScalarCommand(body, pipeline.Key, "name", pipeline.Name)));
        }

        // Assert.
        Assert.All(results, result => Assert.True(result.IsSuccess, result.Error));
        Assert.Equal(original, await editing.BytesAsync());
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryEdit_UndoneOnce_GivesTheBytesBack(string name)
    {
        // Arrange.
        var entry = await DatabricksParityCorpus.EntryAsync(name, TestContext.Current.CancellationToken);
        if (!entry.IsUsable) return;

        var edits = Edits(entry.Model);

        // Act and assert, each edit on a copy of its own.
        foreach ((string label, Func<string, ICommand> edit) in edits)
        {
            using var editing = Copy(name);
            var original = await editing.BytesAsync();
            var result = await editing.RunAsync(edit);
            if (!result.IsSuccess)
            {
                Assert.Equal(original, await editing.BytesAsync());
                continue;
            }

            var undone = await editing.UndoAsync(result);
            Assert.True(undone.IsSuccess, $"{label}: {undone.Error}");
            var after = await editing.BytesAsync();
            Assert.True(original.AsSpan().SequenceEqual(after), $"{label} undone did not give the bytes back.");
        }
    }

    private static DatabricksEditing Copy(string name) =>
        DatabricksEditing.Copying(DatabricksParityCorpus.PathOf(name));

    /// <summary>The edits the corpus document of <paramref name="model"/> takes: of each kind, on its first job, task and pipeline.</summary>
    private static List<(string Label, Func<string, ICommand> Edit)> Edits(DatabricksModel model)
    {
        var edits = new List<(string, Func<string, ICommand>)>
        {
            ("bundle name", body => new SetDatabricksBundleNameCommand(body, "round-trip")),
        };
        foreach (var job in model.Jobs.Take(1))
        {
            edits.Add(("insert task", body => new InsertDatabricksTaskCommand(body, job.Key, "round_trip", "notebook", "notebooks/round_trip")));
            foreach (var task in job.Tasks.Where(task => task.Key.Length > 0).Take(1))
            {
                edits.Add(("rename task", body => new RenameDatabricksTaskCommand(body, job.Key, task.Key, "round_trip")));
                edits.Add(("remove task", body => new RemoveDatabricksTaskCommand(body, job.Key, task.Key)));
                edits.Add(("run_if", body => new SetDatabricksRunIfCommand(body, job.Key, task.Key, "ALL_DONE")));
                edits.Add(("cluster", body => new SetDatabricksClusterCommand(body, job.Key, task.Key, "")));
                foreach (var other in job.Tasks.Where(other => other.Key.Length > 0 && other.Key != task.Key).Take(1))
                {
                    edits.Add(("connect", body => new ConnectDatabricksTasksCommand(body, job.Key, task.Key, other.Key)));
                }

                foreach (var dependency in task.DependsOn.Take(1))
                {
                    edits.Add(("disconnect", body => new DisconnectDatabricksTasksCommand(body, job.Key, dependency.TaskKey, task.Key)));
                }
            }
        }

        foreach (var pipeline in model.Pipelines.Take(1))
        {
            edits.Add(("schema", body => new SetDatabricksPipelineScalarCommand(body, pipeline.Key, "schema", "round_trip")));
            edits.Add(("insert library", body => new InsertDatabricksLibraryCommand(body, pipeline.Key, "notebook", "transformations/round_trip")));
            foreach (var library in pipeline.Libraries.Take(1))
            {
                edits.Add(("remove library", body => new RemoveDatabricksLibraryCommand(body, pipeline.Key, library.Kind, library.Path)));
            }
        }

        return edits;
    }
}
