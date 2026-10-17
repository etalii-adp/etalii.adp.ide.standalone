using System.Globalization;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.HelmChart.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written ones, finding for finding and in order
/// (helm-chart-disl-fbl, task 6, Requirements 5.2 to 5.4 and 5.8): code, severity, file, line and sentence, for
/// every chart folder of the parity corpus and for the <c>Chart.yaml</c> shapes the corpus does not hold. A
/// difference names the chart and both lists.
/// </summary>
/// <remarks>
/// The corpus and the empty directories made in each copy are the transcript's (<see cref="HelmTranscript"/>), so
/// this compares what the transcript records with the hand-written rule set beside it. Since task 9 the two sides
/// read a folder apart: the oracle with today's reader (<see cref="HandWrittenHelmReader"/>), the product through
/// the binding and its plugin (<see cref="HelmFolder.Reading(string)"/>).
/// </remarks>
public class HelmDerivedFindingsTests
{
    public static TheoryData<string> Charts() => [.. HelmTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Charts))]
    public void EveryFinding_IsTheHandWrittenOne(string chart)
    {
        using var copy = Copy(chart);

        // Act and assert.
        AssertSame(chart, copy.Path);
    }

    /// <summary>
    /// What the corpus does not hold: a <c>Chart.yaml</c> that does not parse, one the reader takes nothing from (empty,
    /// and a list), one whose name is written empty, a library chart and a legacy one, each with no templates.
    /// </summary>
    /// <remarks>
    /// The two the reader takes nothing from were held apart as a defect of the bundled definition from task 6 until the
    /// definition's <c>helm.empty-templates</c> rule guarded on readable metadata (etalii.adp, task 11).
    /// </remarks>
    public static TheoryData<string, string> Metadata() => new()
    {
        { "unparseable", "apiVersion: v2\nname: [unclosed\n" },
        { "empty", "" },
        { "a list", "- apiVersion: v2\n" },
        { "an empty name", "apiVersion: v2\nname:\nversion: 1.0.0\n" },
        { "a library", "apiVersion: v2\nname: lib\nversion: 1.0.0\ntype: library\n" },
        { "a legacy chart", "apiVersion: v1\nname: old\nversion: '1.0'\n" },
    };

    [Theory]
    [MemberData(nameof(Metadata))]
    public async Task EveryFinding_IsTheHandWrittenOne_WhateverChartYamlHolds(string what, string chartYaml)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var folder = TestFolder.Create("EtAlii.Adp.HelmDerivedFindings");
        await File.WriteAllTextAsync(folder.Combine("Chart.yaml"), chartYaml, cancellationToken);
        await File.WriteAllTextAsync(folder.Combine("values.yaml"), "replicas: 1\n", cancellationToken);

        // Act and assert.
        AssertSame(what, folder.Path);
    }

    [Fact]
    public void ARegisteredFolderThatIsNotAChart_IsTheHandWrittenOne() =>
        AssertSame("not a chart", HelmFolder.Fixture("not-a-chart"));

    /// <summary>The findings derived from <paramref name="folder"/> read through the binding are the hand-written ones of today's reading of it, in order.</summary>
    private static void AssertSame(string what, string folder) =>
        AssertSame(what, HandWrittenHelm.Findings(new HandWrittenHelmReader().Read(folder)), HelmDefinition.Findings(HelmFolder.Reading(folder)));

    /// <summary>The derived findings of <paramref name="read"/>, a chart made in memory, are the hand-written ones, in order.</summary>
    internal static void AssertSame(string what, HelmChart read) =>
        AssertSame(what, HandWrittenHelm.Findings(read), HelmDefinition.Findings(HelmFolder.Reading(read)));

    private static void AssertSame(string what, IEnumerable<DiagramProblem> handWritten, IEnumerable<DiagramProblem> derived)
    {
        var expected = handWritten.Select(Line).ToList();
        var actual = derived.Select(Line).ToList();
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{what}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    private static string Line(DiagramProblem problem)
    {
        var where = problem.Location is DiagramProblemFileLocation located
            ? $"{located.RelativePath}:{located.Line.ToString(CultureInfo.InvariantCulture)}"
            : problem.Location?.ToString() ?? "-";
        return $"{problem.RuleId} | {problem.Severity} | {where} | {problem.Message}";
    }

    /// <summary>A copy of <paramref name="chart"/>, a path relative to <c>src/</c>, with the empty directories git cannot store made in it.</summary>
    private static TestFolder Copy(string chart)
    {
        var source = Path.Combine(HelmTranscript.SourceFolder, chart);
        var copy = TestFolder.Create("EtAlii.Adp.HelmDerivedFindings");
        TestFolder.CopyTree(source, copy.Path);
        foreach (var directory in HelmTranscript.EmptyDirectories.GetValueOrDefault(Path.GetFileName(source), []))
        {
            Directory.CreateDirectory(copy.Combine(directory));
        }

        return copy;
    }
}
