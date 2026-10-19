using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests.Parity;

/// <summary>
/// The findings derived from the DISL definition over the binding's reading equal the hand-written ones
/// (causal-loop-disl-fbl Requirement 3.3, Q4): for every document of the parity corpus, finding for finding,
/// with its severity, sentence, code and line, in the order the validator gave them; the unlabelled loops
/// included, whose order is the plugin function's (<see cref="CausalLoopCycles"/>).
/// </summary>
/// <remarks>
/// Where the hand-written findings throw - a document that declares one variable twice (finding A7) - the
/// derived ones are required not to, and to be none; that is the one place the two are not compared.
/// </remarks>
public class CausalLoopDerivedFindingsTests
{
    public static TheoryData<string> Documents() => [.. CausalLoopTranscript.Corpus()];

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task EveryFinding_IsTheHandWrittenOne(string relative)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);

        // Act.
        var actual = (await Validate(text)).Select(Text).ToList();
        List<string> expected;
        try
        {
            expected = [.. HandWrittenCausalLoopFindings.Of(text).Select(Text)];
        }
        catch (ArgumentException)
        {
            // Finding A7: the derived findings were read without throwing, and are none.
            Assert.Empty(actual);
            return;
        }

        // Assert.
        Assert.True(
            expected.SequenceEqual(actual, StringComparer.Ordinal),
            $"{relative}:\nexpected {string.Join("\n         ", expected)}\nactual   {string.Join("\n         ", actual)}");
    }

    [Fact]
    public async Task TheUnlabelledLoops_AreReportedInTheToolsOrder_NotDislsSortedOne()
    {
        // Arrange: weight-forms.cld is the document of the corpus on which the two orders differ.
        var relative = CausalLoopTranscript.Corpus().Single(path => path.EndsWith("/weight-forms.cld", StringComparison.Ordinal));
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);

        // Act.
        var loops = (await Validate(text))
            .Where(problem => problem.RuleId == CausalLoopValidator.UnlabelledLoopRuleId)
            .Select(problem => problem.Message.Split(" through ")[1].Split(" that ")[0])
            .ToList();

        // Assert.
        Assert.Equal(["a → b → c → d", "a → b → c", "a → b → d", "a → b", "a → c → d", "a → c → d → b", "a → c", "b → c → d", "b → d"], loops);
    }

    [Fact]
    public async Task ADocumentThatDeclaresAVariableTwice_HasNoFindings_WhereTheHandWrittenOnesThrew()
    {
        // Arrange.
        var relative = CausalLoopTranscript.Corpus().Single(path => path.EndsWith("/duplicate-variable.cld", StringComparison.Ordinal));
        var text = await File.ReadAllTextAsync(Path.Combine(CausalLoopTranscript.SourceFolder, relative), TestContext.Current.CancellationToken);

        // Act.
        var oracle = Record.Exception(() => HandWrittenCausalLoopFindings.Of(text));
        var findings = await Validate(text);

        // Assert.
        Assert.IsType<ArgumentException>(oracle);
        Assert.Empty(findings);
    }

    private static async Task<IReadOnlyList<DiagramProblem>> Validate(string text) =>
        await new CausalLoopValidator(Diagram.CausalLoop.Origin).ValidateAsync(
            new DiagramValidationRequest(text, "parity", default, "parity.cld", null),
            TestContext.Current.CancellationToken);

    private static string Text(DiagramProblem problem) =>
        $"{(problem.Location as DiagramProblemLineLocation)?.Number.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "nowhere"} | {problem.Severity} | {problem.RuleId} | {problem.Message}";
}
