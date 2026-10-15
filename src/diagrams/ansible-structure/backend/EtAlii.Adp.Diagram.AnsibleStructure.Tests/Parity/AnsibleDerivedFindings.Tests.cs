using System.Globalization;
using Xunit;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition equal the hand-written ones, finding for finding and in order
/// (ansible-structure-disl-fbl, task 6, Requirements 3.2 and 3.3): code, severity, file, line and sentence, for
/// every folder of the parity corpus. A difference names the folder and both lists.
/// </summary>
/// <remarks>
/// The corpus, and what is made in each copy where git cannot hold it, are the transcript's
/// (<see cref="AnsibleTranscript"/>), so this compares what the transcript records with the hand-written rule set
/// beside it.
/// </remarks>
public class AnsibleDerivedFindingsTests
{
    public static TheoryData<string> Folders() => [.. AnsibleTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryFinding_IsTheHandWrittenOne(string folder)
    {
        using var copy = TestFolder.Create("EtAlii.Adp.AnsibleDerivedFindings");
        var made = copy.Combine("folder");
        AnsibleTranscript.MakeCopy(folder, made);

        // Arrange.
        var project = new AnsibleProjectReader().Read(made);

        // Act.
        var expected = HandWrittenAnsible.Findings(project).Select(Line).ToList();
        var actual = AnsibleFindings.For(AnsibleModels.ModelOf(project), AnsibleModels.Findings(project)).Select(Line).ToList();

        // Assert.
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{folder}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    private static string Line(DiagramProblem problem)
    {
        var where = problem.Location is DiagramProblemFileLocation located
            ? $"{located.RelativePath}:{located.Line.ToString(CultureInfo.InvariantCulture)}"
            : problem.Location?.ToString() ?? "-";
        return $"{problem.RuleId} | {problem.Severity} | {where} | {problem.Message}";
    }
}
