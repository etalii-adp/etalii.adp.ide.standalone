using System.Text;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using Xunit;

namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>
/// The module's binding, <c>sankey.fbl</c>, as the requirements' appendix drafts it without its <c>"as": "text"</c>
/// markers (sankey-disl-fbl, task 2). No product class reads it yet; these tests load it from the module's assembly and
/// close the four points the appendix leaves open by planning edits through <see cref="OpenBody"/>.
/// </summary>
public class SankeyBindingTests
{
    private static readonly Lazy<FblBinding> _binding = new(Load);

    private const string Document = "sankey: 1\r\nformat: \"{value} TWh\"\r\nnodes:\r\n  - id: a\r\n    name: A\r\n  - id: b\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: 5\r\n";

    private const string BadValues = "sankey: 1\r\nthickness: wide\r\nflow-color: sideways\r\nformat: x\r\nnodes:\r\n  - id: a\r\n  - id: b\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: lots\r\n  - from: b\r\n    to: a\r\n    value: 3\r\n";

    [Fact]
    public void TheEmbeddedBinding_LoadsWithNoDiagnostic()
    {
        // Arrange.
        using var stream = typeof(SankeyParser).Assembly.GetManifestResourceStream("sankey.fbl");

        // Act.
        var problems = FblDocumentLoader.Load(Bytes(stream!), out var document);

        // Assert.
        Assert.Empty(problems);
        Assert.Equal(["skv"], document!.Bindings.Keys);
    }

    [Fact]
    public void AThicknessTheRootMappingHasNot_IsWrittenAfterTheAttributesBeforeIt_NotUnderTheHeader()
    {
        // Arrange.
        var body = Open(Document);

        // Act.
        var result = body.Change(new ModelChange.Set(DocumentId(body), new Dictionary<string, object?> { ["thickness"] = 1.5 }));

        // Assert: FBL places the new key by the order of the rule's attributes, so it follows format, where the module
        // writes it directly under the sankey: line (finding B12).
        Assert.IsType<PlanResult.Planned>(result);
        Assert.StartsWith("sankey: 1\r\nformat: \"{value} TWh\"\r\nthickness: 1.5\r\nnodes:\r\n", Text(body), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRuleAtTheRoot_WritesAKeyWithoutAnInsert()
    {
        // Arrange: a root mapping that holds the header only.
        var body = Open("sankey: 1\r\n");

        // Act.
        var result = body.Change(new ModelChange.Set(DocumentId(body), new Dictionary<string, object?> { ["thickness"] = 2.0 }));

        // Assert: the document rule declares no insert, and the key is still planned, after the header.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("sankey: 1\r\nthickness: 2\r\n", Text(body));
    }

    [Fact]
    public void TheTemplatesEmptyFlowSequences_AreOpenedByTheFirstEntry()
    {
        // Arrange: the binding's template, which is the module's new document (finding B17).
        var template = _binding.Value.Template!.Text;
        var nodes = Open(template);
        var flows = Open(template);

        // Act.
        var node = nodes.Change(new ModelChange.Add("Node", "n1", new Dictionary<string, object?> { ["name"] = "N" }));
        var flow = flows.Change(new ModelChange.Add("Flow", null, new Dictionary<string, object?> { ["from"] = "a", ["to"] = "b", ["value"] = 1.0 }));

        // Assert: this library opens nodes: [] and flows: [] into block sequences, which FBL 4.3 and 6.1 do not state
        // (finding B13, gap 5); the other list is left as it was.
        Assert.Equal(SankeyParser.EmptyDocument, template);
        Assert.IsType<PlanResult.Planned>(node);
        Assert.Equal("sankey: 1\r\nnodes:\r\n  - id: n1\r\n    name: \"N\"\r\nflows: []\r\n", Text(nodes));
        Assert.IsType<PlanResult.Planned>(flow);
        Assert.Equal("sankey: 1\r\nnodes: []\r\nflows:\r\n  - from: a\r\n    to: b\r\n    value: 1\r\n", Text(flows));
    }

    [Fact]
    public void OneBadValue_CostsNothingUntyped_AndTheWholeEntryOnceTheDefinitionTypesIt()
    {
        // Arrange.
        var untyped = Open(BadValues);
        var typed = Open(BadValues, (_, attribute) => attribute is "thickness" or "value" or "step" ? "number" : null);

        // Act.
        var root = untyped.Model.Elements.Single(element => element.Type == "Document");
        var typedTypes = typed.Model.Elements.Select(element => element.Type).ToList();

        // Assert: read untyped, as this binding is with nothing else beside it, a bad value is the text it was written
        // with and costs nothing; read with the types the definition gives (as DislModelBuilder will), FBL 7.4 makes the
        // root mapping an unreadable entry, so thickness: wide costs every document-wide setting, and value: lots its
        // flow (finding B3, gap 2).
        Assert.Equal("wide", root.Attributes["thickness"]);
        Assert.Equal("sideways", root.Attributes["flowColor"]);
        Assert.Equal(2, untyped.Model.Elements.Count(element => element.Type == "Flow"));
        Assert.Empty(untyped.Model.Findings);
        Assert.Equal(["Node", "Node", "Flow"], typedTypes);
        Assert.Equal(
            ["This entry cannot be read: 'thickness' is not a number.", "This entry cannot be read: 'value' is not a number."],
            typed.Model.Findings.Where(finding => finding.Code == "std.unreadableEntry").Select(finding => finding.Message));
    }

    private static FblBinding Load()
    {
        using var stream = typeof(SankeyParser).Assembly.GetManifestResourceStream("sankey.fbl");
        FblDocumentLoader.Load(Bytes(stream!), out var document);
        return document!.Bindings["skv"];
    }

    private static OpenBody Open(string text, Func<string, string, string?>? types = null) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(text), _binding.Value, new FblOptions { FileName = "body.skv", AttributeType = types });

    private static string DocumentId(OpenBody body) => body.Model.Elements.Single(element => element.Type == "Document").Id;

    private static string Text(OpenBody body) => Encoding.UTF8.GetString(body.Bytes);

    private static byte[] Bytes(Stream stream)
    {
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
