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
/// <remarks>
/// A body that cannot be read gives one finding in both, on the same line and with the same lead-in, but
/// its reason is now the FBL library's rather than YamlDotNet's (task 9's named change), so for such a body
/// the reason is compared with what the library said instead of with the old sentence.
/// </remarks>
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
            var body = FdgCorpus.Derived(path).Readable.Document;
            List<string> expected = [.. FdgRuleSet.Breaches(readable.Model).Select(Text).Select(WithoutReason)];
            List<string> actual = [.. FdgDefinition.Findings(body).Select(Text).Select(WithoutReason)];
            var reasons = FdgDefinition.Findings(body).Where(finding => finding.Message.StartsWith(FdgReaderSentences.NotYaml, StringComparison.Ordinal)).ToList();

            // Assert.
            Assert.True(
                expected.SequenceEqual(actual),
                $"{path}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
            Assert.All(reasons, finding => Assert.Contains(body.Model.Findings, said => finding.Message.EndsWith(said.Message.Replace("The body is not well-formed YAML: ", "", StringComparison.Ordinal), StringComparison.Ordinal)));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }

    private static string WithoutReason(string text) =>
        text.IndexOf(FdgReaderSentences.NotYaml, StringComparison.Ordinal) is var at and >= 0 ? text[..(at + FdgReaderSentences.NotYaml.Length)] : text;

    private static string Text(FdgBreach breach) =>
        $"line {Math.Max(breach.Line, 0) + 1} | {(breach.RuleId == FdgRuleIds.UnreadableEntry ? DiagramProblemSeverity.Warning : DiagramProblemSeverity.Error)} | {breach.RuleId} | {breach.Message}";

    private static string Text(DislFinding finding) =>
        $"line {Math.Max(1, finding.Line ?? 1)} | {DislDiagramDefinition.SeverityOf(finding)} | {finding.Code} | {finding.Message}";
}
