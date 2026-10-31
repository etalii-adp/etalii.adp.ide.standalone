using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The commands planned through the binding do what the hand-written writers did (databricks-disl-fbl task 12,
/// Requirement 6): for every readable document of the parity corpus, every command on a sample of its jobs, tasks,
/// pipelines and libraries gives the writers' refusal, or a file that reads as theirs did, except where
/// <see cref="DatabricksOperationComparison"/> says otherwise.
/// </summary>
public sealed class DatabricksDerivedOperationsTests
{
    private const int Sample = 2;
    private const string NoJob = "There is no job in this file any more.";
    private const string NoPipeline = "There is no pipeline in this file any more.";

    /// <summary>The repairs each document holds, by the edit's label: what the writers refused or broke and the command makes (<see cref="DatabricksOperationComparison"/>).</summary>
    private static readonly Dictionary<string, List<string>> _repairs = new(StringComparer.Ordinal)
    {
        // the job's tasks reached through an alias: the writers wrote a file that no longer read (B15), the binding refuses the add.
        ["anchors-and-merge.yml"] = ["insert notebook task into aliased", "insert python task into aliased", "insert sql task into aliased", "insert condition task into aliased"],
        // a bundle written in JSON, where the writers looked for lines of YAML (B12), and the resource adds of the JSON
        // files below.
        ["bundle.json"] = ["insert notebook task into json_job", "insert python task into json_job", "insert sql task into json_job", "insert condition task into json_job", "rename only", "run_if of only to 'ALL_DONE'", "bundle name to 'parity-bundle'", "add jobs resource", "add pipelines resource"],
        // "libraries": [], an empty array on one line (B12).
        ["findings-pipeline.json"] = ["insert notebook library into ", "insert file library into ", "insert glob library into ", "add jobs resource", "add pipelines resource"],
        // tasks: [], depends_on and libraries written as flow sequences (B12).
        ["flow-collections.yml"] = ["insert notebook task into empty_flow", "insert python task into empty_flow", "insert sql task into empty_flow", "insert condition task into empty_flow", "rename a", "remove a", "disconnect a from b", "insert notebook library into flow_pipeline", "insert file library into flow_pipeline", "insert glob library into flow_pipeline", "remove notebook library transformations/one", "remove file library transformations/two.py"],
        // a resource added to a file written in JSON: the writers wrote lines of YAML into it, which no longer read (B15),
        // and the binding refuses the add, whose skeleton a json file cannot hold (FBL 0.14 section 6.2).
        ["legacy-target.json"] = ["add jobs resource", "add pipelines resource"],
        ["library-ids.json"] = ["add jobs resource", "add pipelines resource"],
        ["pipeline.json"] = ["add jobs resource", "add pipelines resource"],
        // depends_on: [{task_key: a}] (B12).
        ["flow-dependencies.yml"] = ["disconnect a from b", "rename a", "remove a"],
        // a settings file written on one line (B12).
        ["settings-one-line.json"] = ["name of  to 'parity_name'", "catalog of  to 'parity_catalog'", "schema of  to 'parity_schema'", "insert notebook library into ", "insert file library into ", "insert glob library into ", "remove notebook library transformations/a", "remove file library transformations/b.py", "add jobs resource", "add pipelines resource"],
    };

    public static TheoryData<string> Documents() => DatabricksParityCorpus.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryCommand_RefusesOrWritesWhatTheHandWrittenOneDid(string name)
    {
        // Arrange.
        var original = await File.ReadAllBytesAsync(DatabricksParityCorpus.PathOf(name), TestContext.Current.CancellationToken);
        var entry = await DatabricksParityCorpus.EntryAsync(name, TestContext.Current.CancellationToken);
        if (!entry.IsUsable) return;

        var model = entry.Model;
        var comparison = new DatabricksOperationComparison(name, original);

        // Act.
        foreach (var job in model.Jobs.Take(Sample))
        {
            await CompareJobAsync(comparison, job);
        }

        foreach (var pipeline in model.Pipelines.Take(Sample))
        {
            await ComparePipelineAsync(comparison, pipeline);
        }

        await CompareBundleAsync(comparison);

        // Assert.
        Assert.True(comparison.Mismatches.Count == 0, $"{name}: {comparison.Mismatches.Count} of {comparison.Compared.Count} differ\n{string.Join("\n", comparison.Mismatches)}");
        Assert.Equal(_repairs.GetValueOrDefault(name, []), comparison.Repairs);
    }

    private static async Task CompareJobAsync(DatabricksOperationComparison comparison, JobModel job)
    {
        var keyed = job.Tasks.Where(task => task.Key.Length > 0).ToList();
        foreach (var type in new[] { "notebook", "python", "sql", "condition", "shiny" })
        {
            await comparison.CompareAsync(
                $"insert {type} task into {job.Key}",
                body => new InsertDatabricksTaskCommand(body, job.Key, "parity_added", type, "parity/source"),
                (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.InsertTask(text, found, "parity_added", type, "parity/source") : NoJob);
        }

        foreach (var task in keyed.Take(Sample))
        {
            var other = keyed.FirstOrDefault(candidate => candidate.Key != task.Key);
            await comparison.CompareAsync(
                $"rename {task.Key}",
                body => new RenameDatabricksTaskCommand(body, job.Key, task.Key, "parity_renamed"),
                (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.RenameTask(text, found, task.Key, "parity_renamed") : NoJob);
            await comparison.CompareAsync(
                $"remove {task.Key}",
                body => new RemoveDatabricksTaskCommand(body, job.Key, task.Key),
                (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.RemoveTask(text, found, task.Key) : NoJob);
            foreach (var runIf in new[] { "ALL_DONE", "" })
            {
                await comparison.CompareAsync(
                    $"run_if of {task.Key} to '{runIf}'",
                    body => new SetDatabricksRunIfCommand(body, job.Key, task.Key, runIf),
                    (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.SetRunIf(text, found, task.Key, runIf) : NoJob);
            }

            foreach (var cluster in job.Clusters.Take(1).Select(cluster => cluster.Key).Append("").Append("parity_missing"))
            {
                await comparison.CompareAsync(
                    $"cluster of {task.Key} to '{cluster}'",
                    body => new SetDatabricksClusterCommand(body, job.Key, task.Key, cluster),
                    (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.SetCluster(text, found, task.Key, cluster) : NoJob);
            }

            if (other is not null)
            {
                await comparison.CompareAsync(
                    $"rename {task.Key} to {other.Key}",
                    body => new RenameDatabricksTaskCommand(body, job.Key, task.Key, other.Key),
                    (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.RenameTask(text, found, task.Key, other.Key) : NoJob);
                foreach (var outcome in new[] { "", "true" })
                {
                    await comparison.CompareAsync(
                        $"connect {other.Key} to {task.Key} '{outcome}'",
                        body => new ConnectDatabricksTasksCommand(body, job.Key, other.Key, task.Key, outcome),
                        (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.Connect(text, found, other.Key, task.Key, outcome) : NoJob);
                }
            }

            foreach (var dependency in task.DependsOn.Take(1))
            {
                await comparison.CompareAsync(
                    $"disconnect {dependency.TaskKey} from {task.Key}",
                    body => new DisconnectDatabricksTasksCommand(body, job.Key, dependency.TaskKey, task.Key),
                    (text, read) => JobOf(read, job.Key) is { } found ? JobWriter.Disconnect(text, found, dependency.TaskKey, task.Key) : NoJob);
            }
        }
    }

    private static async Task ComparePipelineAsync(DatabricksOperationComparison comparison, PipelineModel pipeline)
    {
        foreach ((string key, string value) in new[] { ("name", "parity_name"), ("name", ""), ("catalog", "parity_catalog"), ("schema", "parity_schema"), ("channel", "PREVIEW"), ("serverless", "true"), ("edition", "ADVANCED") })
        {
            await comparison.CompareAsync(
                $"{key} of {pipeline.Key} to '{value}'",
                body => new SetDatabricksPipelineScalarCommand(body, pipeline.Key, key, value),
                (text, read) => PipelineOf(read, pipeline.Key) is { } found ? PipelineWriter.SetScalar(text, found, key, value) : NoPipeline,
                key);
        }

        foreach (var kind in new[] { "notebook", "file", "glob", "wheel" })
        {
            await comparison.CompareAsync(
                $"insert {kind} library into {pipeline.Key}",
                body => new InsertDatabricksLibraryCommand(body, pipeline.Key, kind, "parity/library"),
                (text, read) => PipelineOf(read, pipeline.Key) is { } found ? PipelineWriter.InsertLibrary(text, found, kind, "parity/library") : NoPipeline);
        }

        foreach (var library in pipeline.Libraries.Take(1).Concat(pipeline.Libraries.Skip(1).TakeLast(1)))
        {
            await comparison.CompareAsync(
                $"insert {library.Kind} library {library.Path} again",
                body => new InsertDatabricksLibraryCommand(body, pipeline.Key, library.Kind, library.Path),
                (text, read) => PipelineOf(read, pipeline.Key) is { } found ? PipelineWriter.InsertLibrary(text, found, library.Kind, library.Path) : NoPipeline);
            await comparison.CompareAsync(
                $"remove {library.Kind} library {library.Path}",
                body => new RemoveDatabricksLibraryCommand(body, pipeline.Key, library.Kind, library.Path),
                (text, read) => PipelineOf(read, pipeline.Key) is { } found ? PipelineWriter.RemoveLibrary(text, found, library.Kind, library.Path) : NoPipeline);
        }
    }

    private static async Task CompareBundleAsync(DatabricksOperationComparison comparison)
    {
        foreach (var name in new[] { "parity-bundle", "" })
        {
            await comparison.CompareAsync(
                $"bundle name to '{name}'",
                body => new SetDatabricksBundleNameCommand(body, name),
                (text, read) => BundleWriter.SetName(text, read.Bundle, name));
        }

        foreach (var kind in new[] { "jobs", "pipelines" })
        {
            await comparison.CompareAsync(
                $"add {kind} resource",
                body => new AddDatabricksResourceCommand(body, kind, "parity_resource"),
                (text, read) => BundleWriter.AddResourceSkeleton(text, read.Bundle, kind, "parity_resource"),
                resourcesInAnyOrder: true);
        }
    }

    /// <summary>The job an edit addresses, as the handlers found it before task 12: the named one, or the file's first.</summary>
    private static JobModel? JobOf(DatabricksModel model, string jobKey) =>
        jobKey.Length > 0 ? model.Jobs.FirstOrDefault(job => job.Key == jobKey) : model.Jobs.FirstOrDefault();

    /// <summary>The pipeline an edit addresses, as the handlers found it before task 12: the named one, or the file's first.</summary>
    private static PipelineModel? PipelineOf(DatabricksModel model, string pipelineKey) =>
        pipelineKey.Length > 0 ? model.Pipelines.FirstOrDefault(pipeline => pipeline.Key == pipelineKey) : model.Pipelines.FirstOrDefault();
}
