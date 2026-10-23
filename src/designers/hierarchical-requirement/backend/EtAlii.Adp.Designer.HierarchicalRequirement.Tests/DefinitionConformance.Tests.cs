using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The module against the designer's specification (hierarchical-requirement FR-001, T040): its name, its element types
/// and their attributes, the document's attributes, the enumerations and the operations are those of
/// <c>definition/hierarchical-requirement.des</c>, in both directions; and the bundled definition is what its provenance
/// says.
/// </summary>
/// <remarks>
/// A difference is reported as what the module is <i>missing</i> or as what is <i>unknown to the definition</i>, so the
/// failure says which side has to change. The specification is read here from the file itself, not through the module's
/// loader, which would compare the module with itself.
/// </remarks>
public sealed class DefinitionConformanceTests
{
    private static JsonElement Definition()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.DefinitionFolder, "hierarchical-requirement.des")));
        return document.RootElement.Clone();
    }

    /// <summary>What one side has and the other does not, as the sentences a failure is made of.</summary>
    private static List<string> Differences(string what, IEnumerable<string> definition, IEnumerable<string> module)
    {
        (HashSet<string> defined, HashSet<string> implemented) = (definition.ToHashSet(StringComparer.Ordinal), module.ToHashSet(StringComparer.Ordinal));
        return
        [
            .. defined.Except(implemented).Order(StringComparer.Ordinal).Select(name => $"{what} '{name}' is missing from the module."),
            .. implemented.Except(defined).Order(StringComparer.Ordinal).Select(name => $"{what} '{name}' is unknown to the definition."),
        ];
    }

    [Fact]
    public void TheModulesName_IsTheDefinitions()
    {
        Assert.Equal(HierarchicalRequirementVocabulary.Name, Definition().GetProperty("language").GetProperty("label").GetString());
        Assert.Equal(HierarchicalRequirementDefinition.Origin, Definition().GetProperty("language").GetProperty("origin").GetString());
    }

    [Fact]
    public void TheModulesTypesAndAttributes_AreTheDefinitions()
    {
        // Arrange.
        var types = Definition().GetProperty("metamodel").GetProperty("types");

        // Assert: the definition was read at all, and the two are one, type by type.
        Assert.Equal(7, types.EnumerateObject().Count());
        Assert.Empty(Differences("The type", types.EnumerateObject().Select(type => type.Name), HierarchicalRequirementVocabulary.Types.Keys));
        List<string> differing = [];
        foreach (var type in types.EnumerateObject())
        {
            var implemented = HierarchicalRequirementVocabulary.Types.GetValueOrDefault(type.Name) ?? [];
            differing.AddRange(Differences($"{type.Name}'s attribute", type.Value.GetProperty("attributes").EnumerateObject().Select(attribute => attribute.Name), implemented));
        }
        Assert.Empty(differing);
        var document = Definition().GetProperty("metamodel").GetProperty("document").GetProperty("attributes").EnumerateObject().Select(attribute => attribute.Name);
        Assert.Empty(Differences("The document's attribute", document, HierarchicalRequirementVocabulary.DocumentAttributes));
    }

    [Fact]
    public void TheModulesEnumerations_AreTheDefinitions()
    {
        // Arrange.
        var enums = Definition().GetProperty("metamodel").GetProperty("enums");

        // Assert.
        Assert.Equal(2, enums.EnumerateObject().Count());
        Assert.Empty(Differences("The enumeration", enums.EnumerateObject().Select(e => e.Name), HierarchicalRequirementVocabulary.Enumerations.Keys));
        List<string> differing = [];
        foreach (var enumeration in enums.EnumerateObject())
        {
            var implemented = HierarchicalRequirementVocabulary.Enumerations.GetValueOrDefault(enumeration.Name) ?? [];
            differing.AddRange(Differences($"{enumeration.Name}'s value", enumeration.Value.GetProperty("values").EnumerateObject().Select(value => value.Name), implemented));
        }
        Assert.Empty(differing);
        Assert.Equal(10, HierarchicalRequirementVocabulary.Enumerations["Colour"].Count);
    }

    [Fact]
    public void TheModulesOperations_AreTheDefinitions()
    {
        // Arrange.
        var operations = Definition().GetProperty("operations").EnumerateObject().Select(operation => operation.Name).ToList();

        // Assert.
        Assert.Equal(38, operations.Count);
        Assert.Empty(Differences("The operation", operations, HierarchicalRequirementVocabulary.Operations));
    }

    [Fact]
    public void ADifference_IsNamedInItsDirection()
    {
        // The guard's own control: what the module lacks, and what only the module has, are reported as.
        Assert.Equal(
            ["The operation 'addTag' is missing from the module.", "The operation 'addLink' is unknown to the definition."],
            Differences("The operation", ["setTitle", "addTag"], ["setTitle", "addLink"]));
        Assert.Empty(Differences("The operation", ["a", "b"], ["b", "a"]));
    }

    [Fact]
    public void TheBundledDefinition_IsWhatItsProvenanceSays()
    {
        // Arrange.
        using var provenance = JsonDocument.Parse(HierarchicalRequirementDefinition.Resource("hierarchical-requirement.provenance.json"));

        // Act and assert: each bundled file hashes to what was recorded when it was copied, the companion among them.
        Assert.Equal("etalii-adp/etalii.adp", provenance.RootElement.GetProperty("repository").GetString());
        Assert.Matches("^[0-9a-f]{40}$", provenance.RootElement.GetProperty("revision").GetString());
        var files = provenance.RootElement.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(3, files.Count);
        foreach (var file in files)
        {
            var name = IoPath.GetFileName(file.GetProperty("path").GetString()!);
            Assert.Equal(file.GetProperty("sha256").GetString(), HierarchicalRequirementDefinition.Sha256Of(name));
        }

        // And the specification was read: without its types a resolved checkbox is text, and without many the tags are one word.
        Assert.True(HierarchicalRequirementDefinition.TypedAttributeCount > 20, $"Only {HierarchicalRequirementDefinition.TypedAttributeCount} attribute types were read.");
        Assert.True(HierarchicalRequirementDefinition.IsMany("Requirement", "tags"));
    }

    [Fact]
    public void AMissingResource_Throws_NamingIt()
    {
        // Act.
        var failure = Assert.Throws<InvalidOperationException>(() => HierarchicalRequirementDefinition.Resource("hierarchical-requirement.missing"));

        // Assert.
        Assert.Contains("hierarchical-requirement.missing", failure.Message, StringComparison.Ordinal);
    }
}
