using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Expressions;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Loading;

/// <summary>Loading an FBL document (FBL §2.1, §2.3, §2.7, §14.1; Requirement 2).</summary>
public class LoadingTests
{
    private const string Item = """{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "attributes": { "label": { "key": "label" } } }""";

    private static string Binding(string claims = """{ "extensions": [".t"] }""", string reader = "\"declared\"", string elements = "[" + Item + "]", string extra = "") =>
        $$"""{ "claims": {{claims}}, "body": { "kind": "file", "family": "yaml" }, "reader": {{reader}}, "elements": {{elements}}{{extra}} }""";

    private static IReadOnlyList<LoadDiagnostic> Load(string binding, string version = "0.1") =>
        FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "{{version}}", "bindings": { "t": {{binding}} } }"""), out _);

    [Fact]
    public void AValidDocumentLoadsWithoutAProblem()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.1", "bindings": { "t": {{Binding()}} } }"""), out var document);

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
        Assert.Equal(LoadSeverity.Error, problem.Severity);
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
        Assert.Contains(problems, p => p is { Severity: LoadSeverity.Error, Pointer: "/fbl" });
    }

    [Fact]
    public void ANewerMinorVersionLoadsWithAWarning()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.6", "bindings": { "t": {{Binding()}} } }"""), out var document);

        // Assert.
        Assert.NotNull(document);
        Assert.Equal(LoadSeverity.Warning, Assert.Single(problems).Severity);
    }

    [Theory]
    [InlineData("0.5.1")]
    [InlineData("+0.5")]
    [InlineData(" 0.5")]
    [InlineData("0")]
    public void AVersionNotWrittenAsMajorDotMinorInDigitsIsRefused(string version)
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "{{version}}", "bindings": { "t": {{Binding()}} } }"""), out var document);

        // Assert: exactly major.minor in digits, as FBL §2.7 writes it (behaviour change B-08).
        Assert.Null(document);
        Assert.Equal($"error at /fbl: The FBL version '{version}' is not written as major.minor (FBL §2.7).", Assert.Single(problems).ToString());
    }

    [Fact]
    public void AMissingVersionIsRefused()
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "bindings": { "t": {{Binding()}} } }"""), out var document);

        // Assert.
        Assert.Null(document);
        Assert.Equal("error at /fbl: The document does not say which FBL version it is written in.", Assert.Single(problems).ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"fbl\": \"1.0\", ")]
    [InlineData("\"fbl\": \"0.5.1\", ")]
    public void AVersionThatIsRefusedLeavesTheRestOfTheDocumentReported(string version)
    {
        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ {{version}}"bindings": { "1t": {{Binding()}} } }"""), out var document);

        // Assert: the version's refusal and the binding name's, not only the first (behaviour change B-08).
        Assert.Null(document);
        Assert.Contains(problems, p => p is { Severity: LoadSeverity.Error, Pointer: "/fbl" });
        Assert.Contains(problems, p => p is { Severity: LoadSeverity.Error, Pointer: "/bindings/1t" });
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("0.5")]
    public void AVersionThisLibraryKnowsLoadsWithoutAWarning(string version)
    {
        // Act.
        var problems = Load(Binding(), version);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public void ARuleKeepsTheProgramsItsExpressionsCompiledTo()
    {
        // Arrange.
        const string rule = """{ "name": "item", "type": "Item", "at": "/items/*", "when": "line > 0", "id": { "from": { "key": "id" } }, "attributes": { "label": { "value": "'x'" } } }""";

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.1", "bindings": { "t": {{Binding(elements: "[" + rule + "]")}} } }"""), out var document);

        // Assert: compiled once as the binding loads, for the context a yaml body is read in, and kept for every reading.
        Assert.Empty(problems);
        var item = Assert.Single(document!.Bindings["t"].Elements);
        Assert.NotNull(item.Program(CelContext.Scalars, "line > 0"));
        Assert.NotNull(item.Program(CelContext.Scalars, "'x'"));
        Assert.Null(item.Program(CelContext.Tree, "line > 0"));
    }

    [Theory]
    [InlineData("\"keys\": [\"label\", \"id\"]", "label,id,size")]
    [InlineData("\"emit\": \"{size} {label}\"", "size,label,id")]
    [InlineData("\"skeleton\": \"tags: []\"", "id,label,size")]
    public void ARulesKeyOrderIsItsWireOrderThenItsBindingsKeysInBindingOrder(string insert, string expected)
    {
        // Arrange: three keys at the entry's level and one under its "meta" mapping.
        var rule = $$"""{ "name": "item", "type": "Item", "at": "/items/*", "id": { "from": { "key": "id" } }, "attributes": { "label": { "key": "label" }, "deep": { "key": "deep", "child": "meta" }, "size": { "key": "size" } }, "insert": { "container": "/items", "place": "end", {{insert}} } }""";

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.1", "bindings": { "t": {{Binding(elements: "[" + rule + "]")}} } }"""), out var document);

        // Assert: insert.keys, or the emit's placeholders, first (FBL §6.1), then id, attributes; a child mapping's keys apart.
        Assert.Empty(problems);
        var item = Assert.Single(document!.Bindings["t"].Elements);
        Assert.Equal(expected, string.Join(",", item.KeyOrder(null)));
        Assert.Equal(["deep"], item.KeyOrder("meta"));
    }

    [Fact]
    public void ReadQuotedAndFallbackAreReadFromAnAttributeBinding()
    {
        // Arrange.
        var elements = """
            [{ "name": "item", "type": "Item", "at": "/items/*", "attributes": {
              "label": { "key": "label", "read": "text" },
              "row": { "key": "row", "read": "typed", "quoted": "convert", "fallback": { "value": 0, "report": false } },
              "size": { "key": "size", "fallback": { "value": "wide" } },
              "plain": { "key": "plain" }
            } }]
            """;

        // Act.
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes($$"""{ "fbl": "0.5", "bindings": { "t": {{Binding(elements: elements)}} } }"""), out var document);

        // Assert.
        Assert.Empty(problems);
        var attributes = document!.Bindings["t"].Elements[0].Attributes.ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal((ReadAs.Text, null, null), (attributes["label"].Read, attributes["label"].Quoted, attributes["label"].Fallback));
        Assert.Equal((ReadAs.Typed, QuotedRule.Convert), (attributes["row"].Read, attributes["row"].Quoted));
        Assert.Equal((0, false), (attributes["row"].Fallback!.Value.GetInt32(), attributes["row"].Fallback!.Report));
        Assert.Equal(("wide", true), (attributes["size"].Fallback!.Value.GetString(), attributes["size"].Fallback!.Report));
        Assert.Equal((null, null, null), (attributes["plain"].Read, attributes["plain"].Quoted, attributes["plain"].Fallback));
    }

    [Theory]
    [InlineData("xml", "\"at\": \"/items/item\"", "{ \"attribute\": \"label\", \"read\": \"text\" }", "read")]
    [InlineData("xml", "\"at\": \"/items/item\"", "{ \"attribute\": \"label\", \"quoted\": \"convert\" }", "quoted")]
    [InlineData("xml", "\"at\": \"/items/item\"", "{ \"attribute\": \"label\", \"fallback\": { \"value\": 0 } }", "fallback")]
    [InlineData("lines", "\"line\": \"^(?<label>.*)$\"", "{ \"group\": \"label\", \"read\": \"text\" }", "read")]
    [InlineData("blocks", "\"line\": \"^(?<label>.*)$\"", "{ \"group\": \"label\", \"fallback\": { \"value\": 0 } }", "fallback")]
    [InlineData("yaml", "\"at\": \"/items/*\"", "{ \"value\": \"entry.label\", \"read\": \"text\" }", "read")]
    [InlineData("json", "\"at\": \"/items/*\"", "{ \"value\": \"entry.label\", \"quoted\": \"convert\" }", "quoted")]
    [InlineData("yaml", "\"at\": \"/{name}/*\"", "{ \"capture\": \"name\", \"fallback\": { \"value\": \"x\" } }", "fallback")]
    public void ReadQuotedAndFallbackAreRefusedWhereFbl52ForbidsThem(string family, string match, string slot, string option)
    {
        // Arrange.
        var binding = $$"""{ "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "elements": [{ "name": "item", "type": "Item", {{match}}, "attributes": { "label": {{slot}} } }] }""";

        // Act.
        var problems = Load(binding, "0.5");

        // Assert: one error, at the option.
        var problem = Assert.Single(problems);
        Assert.Equal((LoadSeverity.Error, "/bindings/t/elements/0/attributes/label/" + option), (problem.Severity, problem.Pointer));
    }

    [Theory]
    [InlineData("yaml")]
    [InlineData("json")]
    public void TextAndStyleAreVariablesOfAYamlAndAJsonRule(string family)
    {
        // Arrange.
        var binding = $$"""{ "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "elements": [{ "name": "item", "type": "Item", "at": "/items/*", "when": "text.label == 'yes' && style.label == 'plain'", "id": { "sidecar": { "key": "text.label" } }, "attributes": { "label": { "value": "style.label" } } }] }""";

        // Act.
        var problems = Load(binding, "0.5");

        // Assert.
        Assert.Empty(problems);
    }

    [Theory]
    [InlineData("xml", "\"at\": \"/items/item\"", "text.label == 'x'", "when")]
    [InlineData("xml", "\"at\": \"/items/item\"", "style.label == 'x'", "when")]
    [InlineData("lines", "\"line\": \"^(?<label>.*)$\"", "text.label == 'x'", "when")]
    [InlineData("blocks", "\"line\": \"^(?<label>.*)$\"", "style.label == 'x'", "when")]
    public void TextAndStyleInAnotherFamilyAreALoadProblemAtTheExpression(string family, string match, string expression, string at)
    {
        // Arrange.
        var binding = $$"""{ "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "{{family}}" }, "reader": "declared", "elements": [{ "name": "item", "type": "Item", {{match}}, "{{at}}": "{{expression}}" }] }""";

        // Act.
        var problems = Load(binding, "0.5");

        // Assert.
        var problem = Assert.Single(problems);
        Assert.Equal((LoadSeverity.Error, "/bindings/t/elements/0/" + at), (problem.Severity, problem.Pointer));
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
        Assert.Contains(problems, p => p.Severity == LoadSeverity.Error && p.Pointer == pointer);
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
        Assert.Contains(problems, p => p.Severity == LoadSeverity.Error && p.Pointer == pointer);
    }

    /// <summary>
    /// Input the schema refuses is collected as a problem at its pointer, as every other is (FBL §14.1),
    /// rather than thrown out of the loader: each of these threw before T217, but a registration's resource
    /// that is not an object, which was read as absent.
    /// </summary>
    [Theory]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": { \"container\": \"/items\" } }]", "/bindings/t/elements/0/insert/place")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": { \"place\": 3 } }]", "/bindings/t/elements/0/insert/place")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": { \"place\": \"end\", \"create\": {} } }]", "/bindings/t/elements/0/insert/create/at")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": \"end\" }]", "/bindings/t/elements/0/insert")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"id\": \"id\" }]", "/bindings/t/elements/0/id")]
    [InlineData(", \"elements\": [\"item\"]", "/bindings/t/elements/0")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"number\": { \"decimals\": \"two\" } } } }]", "/bindings/t/elements/0/attributes/n/number/decimals")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"absent\": { \"yaml\": 1 } } } }]", "/bindings/t/elements/0/attributes/n/absent/yaml")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"create\": \"last\" } } }]", "/bindings/t/elements/0/attributes/n/create")]
    [InlineData(", \"elements\": [" + Item + "], \"text\": { \"indent\": 2.5 }", "/bindings/t/text/indent")]
    [InlineData(", \"elements\": [" + Item + "], \"title\": { \"en\": 1 }", "/bindings/t/title")]
    [InlineData(", \"elements\": [" + Item + "], \"template\": { \"text\": \"\", \"byOrigin\": { \"a\": 1 } }", "/bindings/t/template/byOrigin/a")]
    [InlineData(", \"elements\": [" + Item + "], \"header\": \"version\"", "/bindings/t/header")]
    [InlineData(", \"elements\": [" + Item + "], \"registration\": { \"resource\": \"x\" }", "/bindings/t/registration/resource")]
    public void InputTheSchemaRefusesIsAProblemAtItsPointer(string rest, string pointer)
    {
        // Arrange.
        var binding = $$"""{ "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared"{{rest}} }""";

        // Act.
        var problems = Load(binding);

        // Assert.
        Assert.Contains(problems, p => p.Severity == LoadSeverity.Error && p.Pointer == pointer);
    }

    /// <summary>
    /// A setting that names none of its cases, and a slot that names no place or several, are refused by
    /// the schema and are a problem at their pointer: before T218 each loaded without one, the setting
    /// read as its default and the slot as whichever of its places a family looked at first.
    /// </summary>
    [Theory]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"empty\": \"nope\" } } }]", "/bindings/t/elements/0/attributes/n/empty")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"style\": \"bold\" } } }]", "/bindings/t/elements/0/attributes/n/style")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"read\": \"raw\" } } }]", "/bindings/t/elements/0/attributes/n/read")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"quoted\": \"yes\" } } }]", "/bindings/t/elements/0/attributes/n/quoted")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"absent\": { \"yaml\": \"skip\" } } } }]", "/bindings/t/elements/0/attributes/n/absent/yaml")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"create\": { \"emit\": \"<n/>\", \"place\": \"middle\" } } } }]", "/bindings/t/elements/0/attributes/n/create/place")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"attributes\": { \"n\": { \"key\": \"n\", \"override\": { \"key\": \"m\", \"attribute\": \"m\" } } } }]", "/bindings/t/elements/0/attributes/n/override")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": { \"place\": \"middle\" } }]", "/bindings/t/elements/0/insert/place")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": { \"place\": \"before\" } }]", "/bindings/t/elements/0/insert/place")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"insert\": { \"place\": \"end\", \"create\": { \"at\": \"beside\" } } }]", "/bindings/t/elements/0/insert/create/at")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"id\": { \"from\": { \"key\": \"id\", \"text\": true } } }]", "/bindings/t/elements/0/id/from")]
    [InlineData(", \"elements\": [{ \"name\": \"item\", \"type\": \"Item\", \"at\": \"/items/*\", \"source\": { \"parent\": \"id\", \"capture\": \"id\" } }]", "/bindings/t/elements/0/source")]
    [InlineData(", \"elements\": [" + Item + "], \"text\": { \"quote\": \"back\" }", "/bindings/t/text/quote")]
    public void ASettingOrASlotTheSchemaRefusesIsAProblemAtItsPointer(string rest, string pointer)
    {
        // Arrange.
        var binding = $$"""{ "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared"{{rest}} }""";

        // Act.
        var problems = Load(binding);

        // Assert.
        Assert.Contains(problems, p => p.Severity == LoadSeverity.Error && p.Pointer == pointer);
    }

    [Theory]
    [InlineData("\".t\"", "/bindings/t/claims")]
    [InlineData("{ \"extensions\": [\".t\"], \"marker\": { \"rootKey\": \"items\", \"lines\": \"x\" } }", "/bindings/t/claims/marker/lines")]
    [InlineData("{ \"extensions\": [\".t\"], \"readings\": { \"a\": 1 } }", "/bindings/t/claims/readings/a")]
    public void ClaimsTheSchemaRefusesAreAProblemAtTheirPointer(string claims, string pointer)
    {
        // Act.
        var problems = Load(Binding(claims));

        // Assert.
        Assert.Contains(problems, p => p.Severity == LoadSeverity.Error && p.Pointer == pointer);
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
        Assert.DoesNotContain(problems, p => p.Severity == LoadSeverity.Error);
        var chart = document!.Bindings["chart"];
        Assert.Equal(
            [Family.Yaml, Family.Yaml, Family.Yaml, null, Family.Lines, Family.Yaml, Family.Yaml, null, null, null, Family.Yaml, Family.Yaml, Family.Yaml],
            chart.Body.Files.Select(f => f.Family));
        Assert.Equal("^0.1.0", chart.Plugin!.Version);
    }

    [Fact]
    public void VendoredBindingsLoadTheirTitleClaimsRulesAndRegistrationSettings()
    {
        // Act.
        var helm = Vendored("helm-chart.fbl", out var helmVersion)["chart"];
        var structurizr = Vendored("structurizr.fbl", out _)["workspace"];
        var turtle = Vendored("w3c-turtle.fbl", out _)["turtle"];
        var timeline = Vendored("timeline.fbl", out _)["timeline"];

        // Assert: each value as the vendored document declares it.
        Assert.Equal("0.2", helmVersion);
        Assert.Equal("Helm chart", helm.Title);
        Assert.Empty(helm.Claims.Suggest);
        Assert.Equal(["person", "softwareSystem", "container"], structurizr.AllRules.Where(r => r.Opens).Select(r => r.Name));
        Assert.Equal(["language"], turtle.Registration.Headers);
        Assert.False(helm.Registration.CreateOnFirstPlacement);
        Assert.False(timeline.Registration.CreateOnFirstPlacement);
    }

    private static IReadOnlyDictionary<string, FblBinding> Vendored(string file, out string version)
    {
        var problems = FblDocumentLoader.Load(Path.Combine(Repository.Conformance, file), out var document);
        Assert.DoesNotContain(problems, p => p.Severity == LoadSeverity.Error);
        version = document!.Version;
        return document.Bindings;
    }
}
