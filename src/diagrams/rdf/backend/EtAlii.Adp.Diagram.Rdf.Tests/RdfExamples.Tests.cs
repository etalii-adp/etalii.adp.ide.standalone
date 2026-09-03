using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The vendored examples hold their bargain: every document parses and validates clean, every
/// document is registered, the over-budget file really exceeds the budget, and every folder
/// carries the provenance readme and license text the vendoring discipline demands
/// (rdf-diagram Requirement 9).
/// </summary>
public class RdfExamplesTests
{
    /// <summary>The module's examples folder, found by walking up from the test binary.</summary>
    private static string ExamplesFolder { get; } = Locate();

    private static string Locate()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "diagrams", "rdf", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("src/diagrams/rdf/examples was not found above the test binary.");
    }

    private static IEnumerable<string> Documents() =>
        Directory.EnumerateFiles(ExamplesFolder, "*.*", SearchOption.AllDirectories)
            .Where(path => IoPath.GetExtension(path).ToLowerInvariant() is ".ttl" or ".nt");

    public static TheoryData<string> DocumentNames()
    {
        var data = new TheoryData<string>();
        foreach (var path in Documents())
        {
            data.Add(IoPath.GetRelativePath(ExamplesFolder, path));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void EveryDocument_ParsesAndValidatesClean(string relativePath)
    {
        // Arrange.
        var document = RdfDocument.Parse(File.ReadAllText(IoPath.Combine(ExamplesFolder, relativePath)));

        // Act.
        var model = RdfParser.Parse(document);

        // Assert.
        // Validation findings on an example are defects (Requirement 9.4).
        Assert.NotEmpty(model.Triples);
        Assert.Empty(RdfValidator.Judge(model));
    }

    [Theory]
    [MemberData(nameof(DocumentNames))]
    public void EveryDocument_IsRegistered(string relativePath)
    {
        // Arrange.
        var full = IoPath.Combine(ExamplesFolder, relativePath);
        var folder = IoPath.GetDirectoryName(full)!;

        // Act.
        var registered = Directory.EnumerateFiles(folder, "*.adp").Any(adp =>
        {
            var lines = File.ReadAllLines(adp);
            return lines.Length > 0
                && lines[0].Trim() == "w3c/rdf"
                && lines.Skip(1).Any(line => line.Trim() == $"body: {IoPath.GetFileName(full)}");
        });

        // Assert: opening from the explorer needs no setup (Requirement 9.3).
        Assert.True(registered, $"{relativePath} has no w3c/rdf registration naming it.");
    }

    [Fact]
    public void TheLaureatesFile_ReallyExceedsTheBudget()
    {
        // Arrange.
        var document = RdfDocument.Parse(File.ReadAllText(IoPath.Combine(ExamplesFolder, "nobel", "laureates.ttl")));
        var model = RdfParser.Parse(document);

        // Act.
        var projection = RdfProjection.Project(model);

        // Assert: the truncated view has a real file to be honest about (Requirement 8).
        Assert.True(projection.Truncated, $"laureates.ttl draws only {projection.Total} nodes - the budget demonstration needs more than {RdfProjection.DefaultBudget}.");
        Assert.Equal(RdfProjection.DefaultBudget, projection.Shown);
    }

    [Fact]
    public void EveryFolder_CarriesItsProvenanceAndLicense()
    {
        // Arrange.
        var folders = Documents().Select(IoPath.GetDirectoryName).Distinct().Cast<string>();

        // Act & assert: the vendoring discipline's paper trail (Requirement 9.5, 9.6).
        Assert.All(folders, folder =>
        {
            Assert.True(File.Exists(IoPath.Combine(folder, "readme.md")), $"{folder} has no provenance readme.");
            Assert.True(File.Exists(IoPath.Combine(folder, "LICENSE.md")), $"{folder} carries no license text.");
            var readme = File.ReadAllText(IoPath.Combine(folder, "readme.md"));
            Assert.Contains("Retrieved", readme);
            Assert.Contains("License", readme);
        });
    }

    [Fact]
    public void TheWalk_StillFindsTheDocuments()
    {
        // Assert: if discovery ever comes up empty, every theory above passes vacuously.
        Assert.True(Documents().Count() >= 5, "The examples walk found fewer documents than the vendored set holds.");
    }
}
