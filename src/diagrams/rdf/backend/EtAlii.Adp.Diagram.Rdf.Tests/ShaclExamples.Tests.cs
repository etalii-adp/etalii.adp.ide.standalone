using EtAlii.Adp.Diagram.Rdf.Shacl;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The vendored shapes graphs (shacl-diagram Requirement 9), guarded on the bargain that makes
/// vendoring worth doing: real published documents exercise input the author never imagined, so
/// they must actually parse, project and validate here - and the paperwork that makes them
/// redistributable must travel with them.
/// </summary>
public class ShaclExamplesTests
{
    private static readonly DiagramOrigin _origin = new("w3c", "shacl");

    /// <summary>The examples folder, found from the test binary rather than from a hard-coded depth.</summary>
    private static string ExamplesRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(IoPath.Combine(directory.FullName, "src", "diagrams")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return IoPath.Combine(directory.FullName, "src", "diagrams", "rdf", "examples", "shacl");
    }

    public static TheoryData<string> Folders() => new("w3c-shacl", "fair-data-point");

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryVendoredFolder_CarriesItsLicenceFileAndProvenance(string folder)
    {
        var path = IoPath.Combine(ExamplesRoot(), folder);

        // The licence file itself, not a line in a readme naming one: the terms travel with the
        // bytes, so whoever finds the folder later has them in hand.
        var licence = IoPath.Combine(path, "LICENSE.md");
        Assert.True(File.Exists(licence), $"{folder} has no vendored LICENSE.md file");
        Assert.True(new FileInfo(licence).Length > 500, $"{folder}'s LICENSE looks truncated");

        var readme = File.ReadAllText(IoPath.Combine(path, "readme.md"));
        Assert.Contains("Source", readme, StringComparison.Ordinal);
        Assert.Contains("Retrieved", readme, StringComparison.Ordinal);
        Assert.Contains("License", readme, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public async Task EveryVendoredShapesGraph_ParsesAndValidatesClean(string folder)
    {
        var path = IoPath.Combine(ExamplesRoot(), folder);
        var bodies = Directory.GetFiles(path, "*.ttl");
        Assert.NotEmpty(bodies);

        foreach (var body in bodies)
        {
            var text = await File.ReadAllTextAsync(body, TestContext.Current.CancellationToken);
            var model = RdfParser.Parse(LineDocument.Parse(text));
            Assert.NotEmpty(model.Triples);

            // Every registration resolves, and the reading draws something.
            var registration = IoPath.ChangeExtension(body, ".adp");
            Assert.True(File.Exists(registration), $"{body} has no .adp registration");
            Assert.StartsWith("w3c/shacl", await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);

            Assert.NotEmpty(ShaclProjection.Project(model).Cards);

            // Nothing above info: a real file has to be clean, not merely parseable.
            var problems = await new ShaclValidator(_origin).ValidateAsync(
                new DiagramValidationRequest(text, IoPath.GetFileNameWithoutExtension(body), path, body, registration),
                TestContext.Current.CancellationToken);

            Assert.DoesNotContain(problems, problem => problem.Severity > DiagramProblemSeverity.Info);
        }
    }

    [Fact]
    public void TheSpecCorpus_ExercisesRequirement94sConstructList()
    {
        var model = RdfParser.Parse(LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(ExamplesRoot(), "w3c-shacl", "spec-examples.ttl"))));
        var projection = ShaclProjection.Project(model);

        // Blank-node property shapes, drawn as rows of the card that references them.
        Assert.Contains(projection.Cards, card => card.Rows.Any(row => row.Blank));

        // Targets of at least two kinds.
        var kinds = projection.Cards.SelectMany(card => card.Targets).Select(chip => chip.Kind).Distinct().ToArray();
        Assert.Contains(ShaclTargetKind.Class, kinds);
        Assert.Contains(ShaclTargetKind.Node, kinds);

        // A logical combinator, and a complex path printed as SHACL path syntax rather than
        // expanded into its rdf:first/rdf:rest plumbing.
        Assert.Contains(projection.Cards, card => card.Rows.Any(row => row.Summary.Contains("or(", StringComparison.Ordinal)));
        Assert.Contains(projection.Cards, card => card.Rows.Any(row => row.Path.Contains('/', StringComparison.Ordinal)));
    }

    [Fact]
    public void TheDeployedShapes_DrawTheirOutwardTargetsAsChips_AndTheirCyclesAsEdges()
    {
        var model = RdfParser.Parse(LineDocument.Parse(
            File.ReadAllText(IoPath.Combine(ExamplesRoot(), "fair-data-point", "navigation-shapes.ttl"))));
        var projection = ShaclProjection.Project(model);

        // Every target here names a class from another vocabulary, so every chip is honestly
        // "not described in this file" - and not one of them became an element or an edge.
        var chips = projection.Cards.SelectMany(card => card.Targets).ToArray();
        Assert.NotEmpty(chips);
        Assert.All(chips, chip => Assert.False(chip.DescribedInFile));

        var cardIds = projection.Cards.Select(card => card.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(projection.Edges, edge =>
        {
            Assert.Contains(edge.FromId, cardIds);
            Assert.Contains(edge.ToId, cardIds);
        });

        // The sh:node references form a real cycle - catalogue to dataset and back - which this
        // reading draws whole, because it is drawing a description rather than walking a tree.
        Assert.Contains(projection.Edges, edge => edge.Kind == "node");
        Assert.True(projection.Edges.Count >= 4);
    }
}
