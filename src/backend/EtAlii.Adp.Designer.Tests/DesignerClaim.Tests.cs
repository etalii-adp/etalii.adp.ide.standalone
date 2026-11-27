using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using Xunit;

namespace EtAlii.Adp.Designer.Tests;

/// <summary>
/// A designer type's claim, made from its bundled bindings alone (spec 093 R1, FBL sections 12.1 to 12.3): the files it
/// takes by name, those it claims by their marker, the mark a refusal names and whether a registration comes with the first
/// setting. No extension, marker or origin is written for the type.
/// </summary>
public class DesignerClaimTests
{
    private const string BindingsJson = """
        {
          "fbl": "0.7",
          "bindings": {
            "yaml": {
              "claims": {
                "extensions": [".yaml", ".yml"],
                "shared": true,
                "marker": { "rootKey": "designer", "value": "fixture/table" },
                "origins": ["fixture/table"]
              },
              "body": { "kind": "file", "family": "yaml" },
              "reader": "declared",
              "elements": [{ "name": "table", "type": "Table", "at": "/", "attributes": { "name": { "key": "name" } } }],
              "registration": { "createOnFirstPlacement": false }
            },
            "xml": {
              "claims": {
                "extensions": [".xml"],
                "shared": true,
                "marker": { "pattern": "^<ded\\s[^>]*designer=\"fixture/table\"" },
                "origins": ["fixture/table"]
              },
              "body": { "kind": "file", "family": "xml" },
              "reader": "declared",
              "elements": [{ "name": "table", "type": "Table", "at": "/ded", "attributes": { "name": { "attribute": "name" } } }],
              "registration": { "createOnFirstPlacement": false }
            }
          }
        }
        """;

    private static readonly DesignerClaim _claim = new([.. Load().Bindings.Values]);

    [Theory]
    [InlineData("cities.yaml", true)]
    [InlineData("cities.YML", true)]
    [InlineData("cities.xml", true)]
    [InlineData("cities.json", false)]
    [InlineData("cities", false)]
    public void Takes_AFileByTheNamesAndExtensionsOfItsBindings(string fileName, bool takes) =>
        Assert.Equal(takes, _claim.Takes(fileName));

    [Theory]
    [InlineData("cities.yaml", "ded: \"0.1\"\ndesigner: fixture/table\n", true)]
    [InlineData("cities.yaml", "ded: \"0.1\"\ndesigner: fixture/other\n", false)]
    [InlineData("cities.yaml", "name: an ordinary file\n", false)]
    [InlineData("cities.xml", "<ded ded=\"0.1\" designer=\"fixture/table\">\n</ded>\n", true)]
    [InlineData("cities.xml", "<project>\n</project>\n", false)]
    [InlineData("cities.json", "{ \"designer\": \"fixture/table\" }", false)]
    public void Matches_AFileByTheMarkerOfTheBindingThatTakesIt(string fileName, string head, bool matches) =>
        Assert.Equal(matches, _claim.Matches(fileName, Encoding.UTF8.GetBytes(head)));

    [Fact]
    public void Mark_IsTheRootKeyOfAYamlFile_AndNoneForAPattern()
    {
        // Act and assert: a root key is text a person can copy; a pattern is an expression, which a refusal does not quote.
        Assert.Equal("the root key 'designer: fixture/table'", _claim.Mark("cities.yaml"));
        Assert.Null(_claim.Mark("cities.xml"));
        Assert.Null(_claim.Mark("cities.json"));
    }

    [Fact]
    public void CreatesRegistration_IsWhatTheBindingsRegistrationSays() =>
        Assert.False(_claim.CreatesRegistration);

    private static FblDocument Load()
    {
        var diagnostics = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(BindingsJson), out var document);
        Assert.True(document is not null, string.Join("; ", diagnostics));
        return document;
    }
}
