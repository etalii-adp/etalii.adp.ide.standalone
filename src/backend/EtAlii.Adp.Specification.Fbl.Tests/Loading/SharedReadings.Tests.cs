using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Loading;

/// <summary>
/// FBL 0.10's readings of one binding as the loader gives them (sections 5.1, 8.2 and 14.1): a rule's <c>origins</c>, a
/// registration's <c>resource.byOrigin</c>, and the checks that refuse an origin the binding does not claim and a
/// <c>secondary</c> beside an <c>override</c>.
/// </summary>
public class SharedReadingsTests
{
    private static FblBinding Databricks()
    {
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "databricks.fbl"), out var document);
        Assert.DoesNotContain(problems, problem => problem.Severity == LoadSeverity.Error);
        return document!.Bindings["configuration"];
    }

    [Fact]
    public void EachReading_SeesTheRulesItsOriginAdmits_AndItsOwnResourceCapture()
    {
        // Arrange.
        var binding = Databricks();

        // Act.
        var job = binding.ForOrigin("databricks/job");
        var pipeline = binding.ForOrigin("databricks/pipeline");
        var bundle = binding.ForOrigin("databricks/bundle");

        // Assert: one entry is a Resource to the bundle and the job itself to its own reading, each by its own capture.
        Assert.Equal(["job", "task", "cluster", "dependency"], job.AllRules.Select(rule => rule.Name));
        Assert.Equal(["bundle", "jobResource", "pipelineResource", "target"], bundle.AllRules.Select(rule => rule.Name));
        Assert.Equal("job", job.Registration.ResourceCapture);
        Assert.Equal("pipeline", pipeline.Registration.ResourceCapture);
        Assert.Null(bundle.Registration.ResourceCapture);
    }

    [Fact]
    public void AReadingOfNoOrigin_SeesEveryRule_AndEachOriginIsMadeOnce()
    {
        // Arrange.
        var binding = Databricks();

        // Act and assert.
        Assert.Same(binding, binding.ForOrigin(null));
        Assert.Equal(binding.AllRules.Count(), binding.ForOrigin(null).AllRules.Count());
        Assert.Same(binding.ForOrigin("databricks/job"), binding.ForOrigin("databricks/job"));
        Assert.Same(binding.ForOrigin("databricks/job"), binding.ForOrigin("databricks/pipeline").ForOrigin("databricks/job"));
    }

    [Theory]
    [InlineData("origins-not-claimed.fbl", "/bindings/configuration/elements/1/origins")]
    [InlineData("resource-by-origin-not-claimed.fbl", "/bindings/configuration/registration/resource/byOrigin/databricks~1jobs")]
    public void TheVendoredRefusedBinding_IsRefusedForTheOriginItDoesNotClaim(string file, string pointer)
    {
        // Act.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "refused", file), out var document);

        // Assert: refused for that one reason, at its place.
        var error = Assert.Single(problems, problem => problem.Severity == LoadSeverity.Error);
        Assert.Equal(pointer, error.Pointer);
        Assert.Null(document);
    }

    [Fact]
    public void ASecondaryBesideAnOverride_IsRefused()
    {
        // Arrange.
        var json = """
            { "fbl": "0.10", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared",
              "elements": [{ "name": "doc", "type": "Doc", "at": "/",
                "attributes": { "schema": { "key": "schema", "secondary": { "key": "target" }, "override": { "key": "forced" } } } }] } } }
            """;

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), out _);

        // Assert.
        var error = Assert.Single(problems, problem => problem.Severity == LoadSeverity.Error);
        Assert.Equal("/bindings/t/elements/0/attributes/schema/secondary", error.Pointer);
    }
}
