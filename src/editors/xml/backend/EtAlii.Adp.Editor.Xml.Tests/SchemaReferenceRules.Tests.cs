using System.Text.Json;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// The C# twin of the client's schema reference rules, held to the same case file the client's
/// test reads (xml-editor Requirement 9.6), from the same path.
/// </summary>
public class SchemaReferenceRulesTests
{
    private static readonly JsonElement[] Cases = ReadCases();

    public static TheoryData<string> EveryCase()
    {
        var data = new TheoryData<string>();
        foreach (var testCase in Cases)
        {
            data.Add(testCase.GetProperty("name").GetString()!);
        }

        return data;
    }

    [Fact]
    public void TheCaseFile_IsTheOneTheClientReads_AndIsNotEmpty()
    {
        // Arrange and act: an empty or renamed list must fail here, not pass over nothing.
        Assert.True(Cases.Length >= 20, $"the shared case file holds {Cases.Length} cases");
    }

    [Theory]
    [MemberData(nameof(EveryCase))]
    public void EveryCase_IsAnsweredAsTheClientAnswersIt(string name)
    {
        // Arrange.
        var testCase = Cases.Single(candidate => candidate.GetProperty("name").GetString() == name);
        var document = testCase.GetProperty("document").EnumerateArray().Select(segment => segment.GetString()!).ToArray();
        var bundled = testCase.GetProperty("bundled").EnumerateArray().Select(address => address.GetString()!).ToHashSet(StringComparer.Ordinal);
        var expected = testCase.GetProperty("expected");

        // Act.
        var target = SchemaReferenceRules.Resolve(testCase.GetProperty("reference").GetString()!, document, bundled);

        // Assert.
        switch (expected.GetProperty("kind").GetString())
        {
            case "project":
                var project = Assert.IsType<SchemaTarget.Project>(target);
                Assert.Equal(expected.GetProperty("path").EnumerateArray().Select(segment => segment.GetString()!), project.Path);
                break;
            case "bundled":
                Assert.Equal(expected.GetProperty("id").GetString(), Assert.IsType<SchemaTarget.Bundled>(target).Id);
                break;
            default:
                Assert.Equal(expected.GetProperty("reason").GetString(), Assert.IsType<SchemaTarget.None>(target).Reason);
                break;
        }
    }

    private static JsonElement[] ReadCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPaths.Under("src", "fixtures", "schema-reference-cases.json")));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(testCase => testCase.Clone()).ToArray();
    }
}
