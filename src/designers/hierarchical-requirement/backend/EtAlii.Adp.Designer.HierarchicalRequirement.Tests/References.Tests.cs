using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// A reference is text, so the module writes one as it writes any description, and keeps it when its target goes (spec 029
/// T089, FR-026; 028's User Story 6): the steps of <c>hierarchical-requirement-references</c> run through the module's
/// operations make the fixture's bytes, and the document its delete leaves shows each broken reference as one that names
/// nothing, with the finding of <c>references/deleted</c> at its line.
/// </summary>
public sealed class ReferencesTests
{
    private const string Fixture = "hierarchical-requirement-references";

    /// <summary>Every step of the fixture that is not an undo: the step, the gesture, its target and its params.</summary>
    public static TheoryData<int, string, string, string> Steps() => new()
    {
        { 0, TextOperations.SetDescription, "REQ-0003", "Only once [REQ-0002] has confirmed the payment." },
        { 2, TextOperations.SetDescription, "REQ-0001", "The provider's terms are on [REQ-0099](https://example.com/terms), a link and no reference.\nThe card rules are in [REQ-0002]." },
        { 4, StructureOperations.MoveRequirement, "REQ-0002", "item=ITEM-0002|after=REQ-0003" },
        { 6, StructureOperations.DeleteEntry, "REQ-0002", "" },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task AGesture_WritesTheFixturesBytes(int step, string operation, string targetId, string value)
    {
        // Arrange.
        var folder = IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Fixture);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(folder, "fixture.json"), TestContext.Current.CancellationToken));
        var input = await File.ReadAllBytesAsync(IoPath.Combine(folder, "requirements.md"), TestContext.Current.CancellationToken);
        var expected = json.RootElement.GetProperty("steps")[step];

        // Act.
        (byte[]? bytes, string refusal) = TextOperations.Handles(operation)
            ? TextOperations.Apply(input, "requirements.md", operation, targetId, value)
            : StructureOperations.Apply(input, "requirements.md", operation, targetId, Params(value));

        // Assert.
        Assert.Equal("", refusal);
        var written = Encoding.UTF8.GetString(Assert.IsType<byte[]>(bytes));
        if (expected.TryGetProperty("expectFile", out var file))
        {
            Assert.Equal(await File.ReadAllTextAsync(IoPath.Combine(folder, file.GetString()!), TestContext.Current.CancellationToken), written);
        }
        else
        {
            Assert.Equal(expected.GetProperty("expect").GetString(), written);
        }
    }

    [Fact]
    public async Task ADeletedTarget_LeavesItsReferencesAsWritten_NamingNothing_AndAFindingAtEachLine()
    {
        // Arrange: the document the delete of REQ-0002 leaves.
        var folder = IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Fixture, "deleted");
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(folder, "fixture.json"), TestContext.Current.CancellationToken));
        var bytes = await File.ReadAllBytesAsync(IoPath.Combine(folder, "requirements.md"), TestContext.Current.CancellationToken);

        // Act.
        var model = HierarchicalRequirementSurfaceMapper.Map(HierarchicalRequirementBody.Read(bytes, "requirements.md").Document, "Payments", "open", _ => null);

        // Assert: the fixture's std.mentions findings, at its lines, and nothing else.
        var findings = json.RootElement.GetProperty("read").GetProperty("findings").EnumerateArray().Select(finding => $"{finding.GetProperty("rule").GetString()}@{finding.GetProperty("line").GetInt32()}");
        Assert.Equal(findings, model.Findings.Select(finding => $"{finding.Code}@{finding.Line}"));
        Assert.All(model.Findings, finding => Assert.Equal("warning", finding.Severity));

        // The box in ITEM-0001's description names nothing; the assessments' other references still name their entries.
        var checkout = model.Nodes.Single(node => node.Number == "ITEM-0001");
        var broken = Assert.Single(checkout.References);
        Assert.Equal(("[REQ-0002]", ""), (checkout.Description.Substring(broken.Start, broken.Length), broken.TargetId));
        // Its finding stands at ITEM-0001, whose description holds it, so selecting it shows that box (FR-021).
        Assert.Contains(checkout.Id, model.Findings.Select(finding => finding.NodeId));
        // A link that writes a number is no reference: REQ-0001's description holds no box.
        Assert.Empty(model.Nodes.Single(node => node.Number == "REQ-0001").References);
        var named = model.Notes.SelectMany(note => note.References).Select(reference => reference.TargetId).ToList();
        Assert.Contains(model.Nodes.Single(node => node.Number == "REQ-0003").Id, named);
        Assert.Contains(model.Nodes.Single(node => node.Number == "ITEM-0003").Id, named);
        Assert.Contains("", named);
    }

    private static Dictionary<string, string> Params(string parameters) =>
        parameters.Length == 0
            ? []
            : parameters.Split('|').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
}
