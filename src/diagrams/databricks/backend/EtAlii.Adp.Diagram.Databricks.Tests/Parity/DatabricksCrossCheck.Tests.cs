using System.Text;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.History;
using Xunit;

namespace EtAlii.Adp.Diagram.Databricks.Tests.Parity;

/// <summary>
/// The cross-check of the FBL implementation's Requirement 11.6 for Databricks (databricks-disl-fbl, task 1): every file
/// the two vendored Databricks bindings take, against what the module's three parsers read of it through
/// <see cref="DatabricksModel.Read"/>. The job binding is opened once per job the module reads, with that job's key as
/// the <c>resource</c> header, and compared with that job; the resources it captures are compared with the job resources
/// the bundle parser reads. Ids a binding addresses by their place are kept, because two holders of one key are a
/// difference the module draws as one element. Its divergences are listed in
/// <c>EtAlii.Adp.Specification.Fbl.Tests/RealFiles/divergences.json</c>, the one ledger.
/// </summary>
public class DatabricksCrossCheckTests
{
    private const string Job = "databricks-job";

    private const string Pipeline = "databricks-pipeline";

    public static TheoryData<string, string> Pairs()
    {
        var data = new TheoryData<string, string>();
        foreach (var key in (string[])[Job, Pipeline])
        {
            foreach (var file in RealFileCorpus.Files(RealFileCorpus.Find(key))) data.Add(key, file);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void TheBindingsReadTheIdsTheModuleReads(string key, string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(RealFileCorpus.FullPath(file));
        var binding = RealFileCorpus.Binding(RealFileCorpus.Find(key));
        var module = DatabricksModel.Outcome(LineDocument.Parse(Encoding.UTF8.GetString(bytes))).Model;

        // Act.
        var body = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file));
        if (body.Model.Unreadable) return;
        var parts = new List<string>();
        if (key == Job)
        {
            Add(parts, "", ModuleCrossCheck.Difference(OfResources(body.Model), OfBundle(module)));
            foreach (var job in module.Jobs)
            {
                var headers = new Dictionary<string, string> { ["resource"] = job.Key };
                var reading = OpenBody.Open(bytes, binding, RealFileCorpus.Options(binding, file, headers)).Model;
                Add(parts, $"job {job.Key}: ", ModuleCrossCheck.Difference(OfBinding(reading), OfJob(job)));
            }
            if (module.Jobs.Count == 0) Add(parts, "no job: ", ModuleCrossCheck.Difference(OfBinding(body.Model), new Dictionary<string, SortedSet<string>>()));
        }
        else
        {
            Add(parts, "", ModuleCrossCheck.Difference(OfBinding(body.Model), OfPipelines(module)));
        }

        // Assert.
        Divergences.Check("cross-check", key, file, parts.Count == 0 ? null : string.Join(" | ", parts));

        static void Add(List<string> into, string prefix, string? difference)
        {
            if (difference is not null) into.Add(prefix + difference);
        }
    }

    /// <summary>A binding's side: every element and relation, by its type, with the natural id it was read with.</summary>
    private static Dictionary<string, SortedSet<string>> OfBinding(FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var element in model.Elements) ModuleCrossCheck.Add(ids, element.Type, element.Id);
        return ids;
    }

    /// <summary>The job binding's resources: the values of its <c>job</c> capture.</summary>
    private static Dictionary<string, SortedSet<string>> OfResources(FblModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var resource in model.Resources) ModuleCrossCheck.Add(ids, "Resource", resource);
        return ids;
    }

    /// <summary>The bundle parser's job resources, the module's side of <see cref="OfResources"/>.</summary>
    private static Dictionary<string, SortedSet<string>> OfBundle(DatabricksModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var resource in model.Bundle.Resources.Where(resource => resource.Kind == "jobs")) ModuleCrossCheck.Add(ids, "Resource", resource.Key);
        return ids;
    }

    /// <summary>
    /// One job as the job parser reads it, under the ids the module draws: <c>task:</c> and <c>cluster:</c> with the key,
    /// and <c>edge:</c> upstream <c>-&gt;</c> task for every dependency, a missing upstream included.
    /// </summary>
    private static Dictionary<string, SortedSet<string>> OfJob(JobModel job)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var task in job.Tasks.Where(task => task.Key.Length > 0))
        {
            ModuleCrossCheck.Add(ids, "Task", $"task:{task.Key}");
            foreach (var dependency in task.DependsOn) ModuleCrossCheck.Add(ids, "Dependency", $"edge:{dependency.TaskKey}->{task.Key}");
        }
        foreach (var cluster in job.Clusters) ModuleCrossCheck.Add(ids, "JobCluster", $"cluster:{cluster.Key}");
        return ids;
    }

    /// <summary>The pipelines as the pipeline parser reads them: the fixed <c>pipeline</c>, and <c>library:</c> with each library's path.</summary>
    private static Dictionary<string, SortedSet<string>> OfPipelines(DatabricksModel model)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var pipeline in model.Pipelines)
        {
            ModuleCrossCheck.Add(ids, "Pipeline", "pipeline");
            foreach (var library in pipeline.Libraries) ModuleCrossCheck.Add(ids, "Library", $"library:{library.Path}");
        }
        return ids;
    }
}
