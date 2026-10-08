using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Loading;

/// <summary>Loading an FBL document (FBL §2.1, §2.3, §2.7, §14.1; Requirement 2).</summary>
public class LoadingTests
{
    private const string Item = """{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "attributes": { "label": { "key": "label" } } }""";

    private static string Binding(string claims = """{ "extensions": [".t"] }""", string reader = "\"declared\"", string elements = "[" + Item + "]", string extra = "") =>
        $$"""{ "claims": {{claims}}, "body": { "kind": "file", "family": "yaml" }, "reader": {{reader}}, "elements": {{elements}}{{extra}} }""";

    private static IReadOnlyList<LoadProblem> Load(string binding, string version = "0.1") =>
        FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "{{version}}", "bindings": { "t": {{binding}} } }"""), null, out _);

    [Fact]
    public void AValidDocumentLoadsWithoutAProblem()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.1", "bindings": { "t": {{Binding()}} } }"""), null, out var document);

        // Assert: the baseline every refusal below departs from by one change.
        Assert.Empty(problems);
        Assert.Equal("item", Assert.Single(document!.Bindings["t"].Elements).Name);
    }

    [Fact]
    public void ADuplicateKeyAnywhereIsRejectedAtItsPointer()
    {
        // Act.
        var problems = Load(Binding(elements: """[{ "name": "item", "type": "Item", "type": "Other", "at": "/items/*" }]"""));

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal(ProblemSeverity.Error, problem.Severity);
        Assert.Equal("/bindings/t/elements/0/type", problem.Pointer);
    }

    [Theory]
    [InlineData("1.0")]
    [InlineData("2.3")]
    public void AnotherMajorVersionIsRefused(string version)
    {
        // Act.
        var problems = Load(Binding(), version);

        // Assert.
        Assert.Contains(problems, p => p is { Severity: ProblemSeverity.Error, Pointer: "/fbl" });
    }

    [Fact]
    public void ANewerMinorVersionLoadsWithAWarning()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.2", "bindings": { "t": {{Binding()}} } }"""), null, out var document);

        // Assert.
        Assert.NotNull(document);
        Assert.Equal(ProblemSeverity.Warning, Assert.Single(problems).Severity);
    }

    [Theory]
    [InlineData("""[{ "name": "item", "type": "Item", "at": "/items/*", "parent": { "rules": ["missing"], "slot": "parent" } }]""", "/bindings/t/elements/0/parent/rules")]
    [InlineData("""[{ "name": "item", "type": "Item", "at": "/items/*", "remove": { "cascade": ["missing"] } }]""", "/bindings/t/elements/0/remove/cascade")]
    [InlineData("""[{ "name": "item", "type": "Item", "at": "/items/*", "within": ["missing"] }]""", "/bindings/t/elements/0/within")]
    [InlineData("""[{ "name": "item", "type": "Item", "at": "/items/*", "files": ["missing"] }]""", "/bindings/t/elements/0/files")]
    [InlineData("""[{ "name": "item", "type": "Item", "at": "/items/*", "attributes": { "owner": { "key": "owner", "reference": { "to": ["missing"] } } } }]""", "/bindings/t/elements/0/attributes/owner/reference/to")]
    [InlineData("""[{ "name": "item", "type": "Item", "at": "/items/*" }, { "name": "item", "type": "Other", "at": "/others/*" }]""", "/bindings/t/elements/1/name")]
    public void ANameThatDoesNotResolveIsRejectedAtItsPointer(string elements, string pointer)
    {
        // Act.
        var problems = Load(Binding(elements: elements));

        // Assert.
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Error && p.Pointer == pointer);
    }

    [Theory]
    [InlineData("""{ "extensions": [".t"], "shared": true }""", "\"declared\"", "[" + Item + "]", "/bindings/t/claims")]
    [InlineData("""{ "extensions": [".t"], "readings": { "a": { "bare": true }, "b": { "bare": true } } }""", "\"declared\"", "[" + Item + "]", "/bindings/t/claims/readings")]
    [InlineData("""{ "extensions": [".t"] }""", "\"declared\"", "[]", "/bindings/t")]
    [InlineData("""{ "extensions": [".t"] }""", """{ "plugin": "x.y" }""", "[" + Item + "]", "/bindings/t")]
    [InlineData("""{ "extensions": [".t"] }""", "\"declared\"", """[{ "name": "item", "type": "Item", "at": "/items/*", "attributes": { "label": { "key": "label", "text": true } } }]""", "/bindings/t/elements/0/attributes/label")]
    public void AStepSixCheckIsApplied(string claims, string reader, string elements, string pointer)
    {
        // Act.
        var problems = Load(Binding(claims, reader, elements));

        // Assert.
        Assert.Contains(problems, p => p.Severity == ProblemSeverity.Error && p.Pointer == pointer);
    }

    [Fact]
    public void ASharedClaimWithAMarkerIsAccepted()
    {
        // Act.
        var problems = Load(Binding("""{ "extensions": [".t"], "shared": true, "marker": { "rootKey": "items" } }"""));

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void EveryProblemIsReportedRatherThanTheFirst()
    {
        // Act.
        var problems = Load(Binding(
            """{ "extensions": [".t"], "shared": true }""",
            elements: """[{ "name": "item", "type": "Item", "at": "/items/*", "line": "(a)\\1", "remove": { "cascade": ["missing"] } }]"""));

        // Assert: the claim, the rule's two anchors and the cascade, each with its own pointer.
        Assert.Contains(problems, p => p.Pointer == "/bindings/t/claims");
        Assert.Contains(problems, p => p.Pointer == "/bindings/t/elements/0");
        Assert.Contains(problems, p => p.Pointer == "/bindings/t/elements/0/remove/cascade");
        Assert.All(problems, p => Assert.False(string.IsNullOrWhiteSpace(p.Message)));
    }

    [Fact]
    public void AReferenceResolvesAgainstTheReferringDocument()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        var document = folder.Write("bindings/plan.fbl", $$"""{ "fbl": "0.1", "bindings": { "t": {{Binding()}}, "u": {{Binding("""{ "extensions": [".u"] }""")}} } }""");
        var fixture = folder.Write("fixtures/one/fixture.json", "{}");

        // Act.
        var across = FblDocumentLoader.ResolveReference("../../bindings/plan.fbl#u", fixture, out _);
        var within = FblDocumentLoader.ResolveReference("#t", document, out _);

        // Assert.
        Assert.Equal("u", across.Name);
        Assert.Equal("t", within.Name);
        Assert.Throws<InvalidOperationException>(() => FblDocumentLoader.ResolveReference("#missing", document, out _));
    }

    [Fact]
    public void AVendoredFolderBindingLoadsItsFileRulesAndItsPlugin()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, "helm-chart.fbl"), out var document);

        // Assert: the file rules' families and the plugin's version, as helm-chart.fbl declares them.
        Assert.DoesNotContain(problems, p => p.Severity == ProblemSeverity.Error);
        var chart = document!.Bindings["chart"];
        Assert.Equal(
            [Family.Yaml, Family.Yaml, Family.Json, Family.Lines, null, Family.Yaml, null, Family.Yaml, Family.Yaml],
            chart.Body.Files.Select(f => f.Family));
        Assert.Equal("^0.1.0", chart.Plugin!.Version);
    }
}
