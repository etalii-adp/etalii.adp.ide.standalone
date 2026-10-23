using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// <c>setTitle</c> and <c>setDescription</c> make the bytes the conformance fixtures state (spec 029 T066, FR-026, R17):
/// a replace of the one value, and, in a document holding placeholders, their numbers and the mark written by the same
/// edit. Each step is the fixture's own, run through the module's operation from the surface's id, not through the
/// fixture runner's stand-in.
/// </summary>
public sealed class TextOperationsTests
{
    /// <summary>Every step of the corpus that changes one title or one description, by fixture and step.</summary>
    public static TheoryData<string, int> Steps() => new()
    {
        { "hierarchical-requirement-edits", 1 },
        { "hierarchical-requirement-references", 0 },
        { "hierarchical-requirement-references", 2 },
        { "hierarchical-requirement-faults", 1 },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task ASetOfOneText_WritesTheFixturesBytes(string fixture, int step)
    {
        // Arrange: the fixture's input, and its step's one changed value.
        var folder = IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, fixture);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(folder, "fixture.json"), TestContext.Current.CancellationToken));
        var inputName = json.RootElement.GetProperty("input").GetString()!;
        var input = await File.ReadAllBytesAsync(IoPath.Combine(folder, inputName), TestContext.Current.CancellationToken);
        var stated = json.RootElement.GetProperty("steps")[step];
        var set = stated.GetProperty("edit").GetProperty("set");
        var attribute = Assert.Single(set.GetProperty("attributes").EnumerateObject());
        var operation = attribute.Name == "title" ? TextOperations.SetTitle : TextOperations.SetDescription;

        // Act.
        (byte[]? bytes, string refusal) = TextOperations.Apply(input, inputName, operation, set.GetProperty("element").GetString()!, attribute.Value.GetString()!);

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(stated.GetProperty("expect").GetString(), Encoding.UTF8.GetString(Assert.IsType<byte[]>(bytes)));
    }

    [Fact]
    public void ATitleOfTwoLines_IsRefused_AndAnEntryTheFileNoLongerHas_IsAChangeMadeAgainstAnOlderFile()
    {
        var folder = IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, "hierarchical-requirement-edits");
        var input = File.ReadAllBytes(IoPath.Combine(folder, "web-shop.md"));

        Assert.Equal((null, "A title is one line; the description holds the lines below it."), TextOperations.Apply(input, "web-shop.md", TextOperations.SetTitle, "REQ-0004", "One\nTwo"));
        Assert.Equal((null, HierarchicalRequirementGestures.Drifted), TextOperations.Apply(input, "web-shop.md", TextOperations.SetTitle, "REQ-0999", "Gone"));
    }
}
