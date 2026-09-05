using Xunit;

using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The vendored STW extracts, judged by the reading that actually opens them. The sibling
/// <see cref="RdfExamplesTests"/> sweep runs the base RDF validator only, so the SKOS rules
/// never saw these files in a test - and the app duly reported 1,517 findings on an extract
/// every gate called clean. An example the product's own rules complain about is a defect in
/// the example or in the rules, and either way it is this test's to catch.
/// </summary>
public class SkosExamplesTests
{
    private static readonly DiagramOrigin _origin = new("w3c", "skos");

    /// <summary>The stw examples folder, found by walking up from the test binary.</summary>
    private static string StwFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams", "rdf", "examples", "stw");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("src/diagrams/rdf/examples/stw was not found above the test binary.");
    }

    public static TheoryData<string> Documents() => new("business-economics", "geographic-names");

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryStwExtract_ValidatesCleanUnderTheSkosRules(string name)
    {
        // Arrange.
        var folder = StwFolder();
        var path = IoPath.Combine(folder, name + ".ttl");
        var registration = IoPath.Combine(folder, name + ".adp");

        // Act.
        var problems = await new SkosValidator(_origin).ValidateAsync(
            new DiagramValidationRequest(File.ReadAllText(path), name, folder, path, registration),
            TestContext.Current.CancellationToken);

        // Assert.
        // Validation findings on a vendored example are defects. An extract that references
        // concepts it does not carry is the extract's own boundary drawn wrong, not the data's.
        Assert.Empty(problems);
    }
}
