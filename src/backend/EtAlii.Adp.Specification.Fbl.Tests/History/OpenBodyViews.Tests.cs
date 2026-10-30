using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.History;

/// <summary>
/// One open body read as several tool types and resources through one binding (FBL 0.10 §8.2, §9.1): every view is a
/// view of the one body, an edit made in one is planned against its bytes and recorded in its one history, and every
/// other view reads it, an undo in any of them taking it back.
/// </summary>
public class OpenBodyViewsTests
{
    private const string Bundle = "databricks/bundle";
    private const string Job = "databricks/job";

    private static OpenBody Open(string? origin = null, string? resource = null)
    {
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "databricks.fbl"), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var path = Path.Combine(Repository.Conformance, "fixtures", "databricks-resources", "two-jobs-two-pipelines.yml");
        return OpenBody.Open(File.ReadAllBytes(path), document!.Bindings["configuration"], new FblOptions { FileName = "two-jobs-two-pipelines.yml", Origin = origin, Resource = resource });
    }

    private static FblElement Task(FblModel model, string key) => model.Elements.Single(element => element.Type == "Task" && element.Attributes["key"] as string == key);

    [Fact]
    public void TwoViewsOfOneOrigin_ThatSelectDifferentResources_EachShowTheirOwn()
    {
        // Arrange.
        var body = Open(Job);

        // Act.
        var first = body.ModelOf(Job, null);
        var second = body.ModelOf(Job, "job_b");

        // Assert: job_a is the first in document order; job_b's tasks of the same keys are its own.
        Assert.Equal(["ingest", "shape"], first.Elements.Where(e => e.Type == "Task").Select(e => e.Attributes["key"]));
        Assert.Equal(["ingest", "report"], second.Elements.Where(e => e.Type == "Task").Select(e => e.Attributes["key"]));
        Assert.Equal(["job_a", "job_b"], second.Resources);
    }

    [Fact]
    public void AnEditInOneView_IsReadByEveryOther_AndOneUndoTakesItBack()
    {
        // Arrange.
        var body = Open(Bundle);
        var before = body.Bytes;
        var ingest = Task(body.ModelOf(Job, "job_b"), "ingest");

        // Act: an edit in job_b's view, then one in the bundle's, which the body was opened as.
        var set = body.Change(new ModelChange.Set(ingest.Id, new Dictionary<string, object?> { ["runIf"] = "ALL_DONE" }), Job, "job_b");
        var prod = body.Model.Elements.Single(element => element.Type == "Target" && element.Attributes["name"] as string == "prod");
        var mode = body.Change(new ModelChange.Set(prod.Id, new Dictionary<string, object?> { ["mode"] = "development" }));

        // Assert: job_b's ingest has its run_if in its view, job_a's does not in its own, and the bundle reads its edit.
        Assert.IsType<PlanResult.Planned>(set);
        Assert.IsType<PlanResult.Planned>(mode);
        Assert.Equal("ALL_DONE", Task(body.ModelOf(Job, "job_b"), "ingest").Attributes["runIf"]);
        Assert.False(Task(body.ModelOf(Job, null), "ingest").Attributes.ContainsKey("runIf"));
        Assert.Equal("development", body.Model.Elements.Single(e => e.Id == prod.Id).Attributes["mode"]);

        // Act: the one history takes both back, in the order they were made, whichever view made them.
        Assert.IsType<UndoResult.Done>(body.Undo());
        var afterOne = body.ModelOf(Job, "job_b");
        Assert.IsType<UndoResult.Done>(body.Undo());

        // Assert.
        Assert.Equal("ALL_DONE", Task(afterOne, "ingest").Attributes["runIf"]);
        Assert.False(Task(body.ModelOf(Job, "job_b"), "ingest").Attributes.ContainsKey("runIf"));
        Assert.Equal(before, body.Bytes);
    }

    [Fact]
    public void AViewOfAResourceTheBodyDoesNotHold_IsEmpty_AndNotUnreadable()
    {
        // Arrange.
        var body = Open(Bundle);

        // Act.
        var missing = body.ModelOf(Job, "job_c");

        // Assert: nothing of the job is read, the body still is, and it knows which jobs it holds.
        Assert.Empty(missing.Elements);
        Assert.False(missing.Unreadable);
        Assert.Equal(["job_a", "job_b"], missing.Resources);
        Assert.NotEmpty(body.Model.Elements);
    }

    [Fact]
    public void AResourceTheBodyDoesNotHold_LeavesNoElement_NotEvenOneOutsideEveryResource()
    {
        // Arrange: a rule whose entries bind no resource capture beside one whose entries do.
        var json = """
            { "fbl": "0.10", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared",
              "registration": { "resource": { "capture": "job" } },
              "elements": [{ "name": "doc", "type": "Doc", "at": "/", "attributes": { "title": { "key": "title" } } },
                           { "name": "job", "type": "Job", "at": "/jobs/{job}", "attributes": { "name": { "key": "name" } } }] } } }
            """;
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        var bytes = "title: T\njobs:\n  a:\n    name: A\n"u8.ToArray();

        // Act.
        var held = OpenBody.Open(bytes, document!.Bindings["t"], new FblOptions { Resource = "a" });
        var missing = OpenBody.Open(bytes, document.Bindings["t"], new FblOptions { Resource = "b" });

        // Assert: the document is read with the resource it holds, and the reading of one it does not hold is empty.
        Assert.Equal(["Doc", "Job"], held.Model.Elements.Select(element => element.Type));
        Assert.Empty(missing.Model.Elements);
        Assert.False(missing.Model.Unreadable);
    }
}
