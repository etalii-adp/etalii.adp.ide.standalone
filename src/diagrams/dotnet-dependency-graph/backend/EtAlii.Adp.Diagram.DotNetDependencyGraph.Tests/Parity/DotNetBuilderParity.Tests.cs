using System.Collections;
using System.Globalization;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests.Parity;

/// <summary>
/// The shared builder's model equals the hand-built one (dotnet-dependency-graph-disl-fbl, task 5, Requirement
/// 3.2): <see cref="DislModelBuilder"/> over the plugin's elements against <see cref="HandBuiltDotNetModel.Build"/> over
/// the same graph, element for element - type, id, ends, parent and every attribute with its type - for every
/// solution of the transcript's corpus and every hand-built graph, so that a difference names the document and the
/// element.
/// </summary>
/// <remarks>
/// The rows read the builder's model (task 6), so this is what holds that model to the hand-built one, which left
/// the product code for this test project as the oracle.
/// </remarks>
public sealed class DotNetBuilderParityTests
{
    /// <summary>The corpus, as the transcript names it.</summary>
    public static TheoryData<string> Solutions() => [.. DotNetCorpus.Solutions()];

    /// <summary>The hand-built graphs, by name.</summary>
    public static TheoryData<string> Graphs() => [.. DotNetTranscript.HandBuiltGraphs().Select(graph => graph.Name)];

    [Theory]
    [MemberData(nameof(Solutions))]
    public void TheBuildersModelOfASolution_IsTheHandBuiltOne(string solution)
    {
        // Arrange.
        var body = DotNetBody.Open(
            Path.Combine(DotNetCorpus.SourceFolder, solution),
            new SolutionReader(),
            new ProjectReader(),
            new PackageDescriptionReader(DotNetCorpus.FixtureCache));

        // Act.
        var built = body.Disl;
        var oracle = HandBuiltDotNetModel.Build(body.Graph);

        // Assert.
        Assert.Empty(built.Findings);
        Assert.Equal(Lines(oracle), Lines(built.Diagram));
    }

    [Theory]
    [MemberData(nameof(Graphs))]
    public void TheBuildersModelOfAHandBuiltGraph_IsTheHandBuiltOne(string name)
    {
        // Arrange.
        var graph = DotNetTranscript.HandBuiltGraphs().Single(candidate => candidate.Name == name).Graph;

        // Act.
        var built = DislModelBuilder.From(new FblModel(DotNetSolutionPlugin.Elements(graph), [], false), DotNetDefinition.Specification);
        var oracle = HandBuiltDotNetModel.Build(graph);

        // Assert.
        Assert.Empty(built.Findings);
        Assert.Equal(Lines(oracle), Lines(built.Diagram));
    }

    /// <summary>Every element of <paramref name="diagram"/> as one line, in the model's order.</summary>
    private static List<string> Lines(DislDiagram diagram) =>
    [
        .. diagram.Elements.Select(element =>
            $"{element.Type.Name} {element.Id} stored {element.IdIsStored} parent {element.Parent?.Id ?? "none"} "
            + $"{(element.Type.IsRelation ? $"{element.Source?.Id ?? "nothing"} -> {element.Target?.Id ?? "nothing"} " : "")}"
            + string.Join("; ", element.Attributes.OrderBy(attribute => attribute.Key, StringComparer.Ordinal).Select(attribute => $"{attribute.Key} = {Typed(attribute.Value)}"))),
    ];

    /// <summary>A value with its type, so that an <c>int</c> where the oracle holds a <c>long</c> is a difference.</summary>
    private static string Typed(object? value) => value switch
    {
        null => "null",
        string text => $"string '{text}'",
        IEnumerable items => $"{value.GetType().Name} [{string.Join(", ", items.Cast<object?>().Select(Typed))}]",
        IFormattable formattable => $"{value.GetType().Name} {formattable.ToString(null, CultureInfo.InvariantCulture)}",
        _ => $"{value.GetType().Name} {value}",
    };
}
