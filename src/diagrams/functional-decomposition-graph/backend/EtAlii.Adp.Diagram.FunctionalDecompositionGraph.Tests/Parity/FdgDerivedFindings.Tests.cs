using System.Globalization;
using EtAlii.Adp.Diagram.Modules;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.FunctionalDecompositionGraph.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written rule set's
/// (functional-decomposition-graph-disl-fbl task 7, Requirements 3.3 to 3.5), finding for finding: code,
/// severity, sentence, line and order, for every document of the parity corpus, in the invariant culture
/// the transcript is generated in.
/// </summary>
public class FdgDerivedFindingsTests
{
    public static TheoryData<string> Documents() => FdgCorpus.Documents();

    [Theory]
    [MemberData(nameof(Documents))]
    public void EveryFinding_IsTheHandWrittenOne(string path)
    {
        // Arrange.
        (DocumentEntry<LineDocument, FdgModel> readable, _) = FdgCorpus.Entries(path);
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            // Act.
            List<string> expected = [.. FdgRuleSet.Breaches(readable.Model).Select(Text)];
            List<string> actual = [.. FdgDefinition.Findings(readable.Model).Select(Text)];

            // Assert.
            Assert.True(
                expected.SequenceEqual(actual),
                $"{path}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static string Text(FdgBreach breach) =>
        $"line {Math.Max(breach.Line, 0) + 1} | {(breach.RuleId == FdgRuleIds.UnreadableEntry ? DiagramProblemSeverity.Warning : DiagramProblemSeverity.Error)} | {breach.RuleId} | {breach.Message}";

    private static string Text(DislFinding finding) =>
        $"line {Math.Max(1, finding.Line ?? 1)} | {DislDiagramDefinition.SeverityOf(finding)} | {finding.Code} | {finding.Message}";
}
