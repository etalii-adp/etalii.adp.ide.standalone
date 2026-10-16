using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The property rows derived from the DISL definition equal the hand-written ones, row for row (helm-chart-disl-fbl,
/// task 5, Requirements 5.1 and 5.8): for every chart folder of the parity corpus, every node, every relation, the
/// empty id and an unknown one. A difference names the chart, the element and both rows.
/// </summary>
/// <remarks>
/// The corpus and the empty directories made in each copy are the transcript's (<see cref="HelmTranscript"/>), so this
/// compares what the transcript records, and names the element where the transcript would name only a line.
/// </remarks>
public class HelmDerivedRowsTests
{
    private const string Unknown = "parity:unknown";

    public static TheoryData<string> Charts() => [.. HelmTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Charts))]
    public void EveryRow_IsTheHandWrittenOne(string chart)
    {
        using var copy = Copy(chart);

        // Arrange.
        var read = new HelmChartReader().Read(copy.Path);
        var graph = HelmGraph.Derive(read);
        List<string> targets = [.. graph.Nodes.Select(node => node.Id), .. HandWrittenHelm.Edges(read).Select(edge => edge.Id), "", Unknown];

        // Act and assert.
        foreach (var target in targets)
        {
            var expected = HandWrittenHelm.Rows(read, target).Select(row => row.RowLine()).ToList();
            var actual = HelmDefinition.Rows(read, target).Select(row => row.RowLine()).ToList();
            Assert.True(
                expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"{chart} {target}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }

    /// <summary>
    /// What the corpus does not hold: a <c>Chart.yaml</c> the reader takes nothing from, as empty, as a list, as one that
    /// does not parse and as one whose name is written empty. Each is beside a values file, so the chart has a second node.
    /// </summary>
    public static TheoryData<string, string> Metadata() => new()
    {
        { "empty", "" },
        { "a list", "- apiVersion: v2\n" },
        { "unparseable", "apiVersion: v2\nname: [unclosed\n" },
        { "an empty name", "apiVersion: v2\nname:\nversion: 1.0.0\n" },
    };

    [Theory]
    [MemberData(nameof(Metadata))]
    public async Task EveryRow_IsTheHandWrittenOne_WhateverChartYamlHolds(string what, string chartYaml)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = TestFolder.Create("EtAlii.Adp.HelmDerivedRows");
        await File.WriteAllTextAsync(folder.Combine("Chart.yaml"), chartYaml, cancellationToken);
        await File.WriteAllTextAsync(folder.Combine("values.yaml"), "replicas: 1\n", cancellationToken);

        // Arrange.
        var read = new HelmChartReader().Read(folder.Path);
        var graph = HelmGraph.Derive(read);
        Assert.Equal(2, graph.Nodes.Count);

        // Act and assert.
        foreach (var target in graph.Nodes.Select(node => node.Id))
        {
            var expected = HandWrittenHelm.Rows(read, target).Select(row => row.RowLine()).ToList();
            var actual = HelmDefinition.Rows(read, target).Select(row => row.RowLine()).ToList();
            Assert.True(
                expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"{what} {target}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
    }

    [Theory]
    [MemberData(nameof(Charts))]
    public void EveryElement_HasTheWireTypeItIsDeliveredAs(string chart)
    {
        using var copy = Copy(chart);

        // Arrange.
        var read = new HelmChartReader().Read(copy.Path);
        var graph = HelmGraph.Derive(read);

        // Act.
        var delivered = new HelmElementMapper().Elements(read, graph, HelmLayout.Compute(read, graph));

        // Assert.
        Assert.All(delivered, element => Assert.Equal(element.Type, HelmDefinition.WireTypeOf(read, element.Id)));
        Assert.Null(HelmDefinition.WireTypeOf(read, Unknown));
    }

    [Fact]
    public void AWrite_IsRefusedWithTheHandWrittenSentence() =>
        Assert.Equal(HandWrittenHelm.Refusal, HelmDefinition.Refusal);

    /// <summary>A copy of <paramref name="chart"/>, a path relative to <c>src/</c>, with the empty directories git cannot store made in it.</summary>
    private static TestFolder Copy(string chart)
    {
        var source = Path.Combine(HelmTranscript.SourceFolder, chart);
        var copy = TestFolder.Create("EtAlii.Adp.HelmDerivedRows");
        TestFolder.CopyTree(source, copy.Path);
        foreach (var directory in HelmTranscript.EmptyDirectories.GetValueOrDefault(Path.GetFileName(source), []))
        {
            Directory.CreateDirectory(copy.Combine(directory));
        }

        return copy;
    }
}
