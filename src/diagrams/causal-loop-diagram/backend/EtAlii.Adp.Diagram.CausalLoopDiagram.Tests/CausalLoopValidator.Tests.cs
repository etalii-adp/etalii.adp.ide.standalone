using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// What this reading says about a document, and what it refuses to do to one
/// (causal-loop-diagram Requirements 3.3, 3.4, 3.5).
/// </summary>
public class CausalLoopValidatorTests
{
    private static readonly CausalLoopValidator _validator =
        new(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin);

    private static async Task<IReadOnlyList<DiagramProblem>> Judge(params string[] statements)
    {
        var text = string.Join("", statements.Select(statement => statement + "\r\n"));
        return await _validator.ValidateAsync(
            new DiagramValidationRequest(text, "feedback", @"C:\project", @"C:\project\feedback.cld", @"C:\project\feedback.adp"),
            TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<DiagramProblem> Of(IReadOnlyList<DiagramProblem> problems, string ruleId) =>
        [.. problems.Where(problem => problem.RuleId == ruleId)];

    /// <summary>
    /// The finding the specification is built around: the document's claim and the arithmetic
    /// disagree, both are named, and nothing is rewritten.
    /// </summary>
    [Fact]
    public async Task ALabelDisagreeingWithItsArrows_IsReportedWithBothLabels()
    {
        // Arrange & act.
        // One negative link is an odd count, so the arrows make this balancing - but it says R1.
        var problems = await Judge(
            "variable a", "variable b",
            "link a -> b +", "link b -> a -",
            "loop R1 \"claims to reinforce\" a b");

        // Assert.
        var finding = Assert.Single(Of(problems, CausalLoopValidator.LabelDisagreesRuleId));
        Assert.Contains("labelled reinforcing", finding.Message, StringComparison.Ordinal);
        Assert.Contains("make it balancing", finding.Message, StringComparison.Ordinal);
        Assert.Contains("1 negative link", finding.Message, StringComparison.Ordinal);
        // And it says why, so a reader can check the rule rather than take the verdict on trust.
        Assert.Contains("even count", finding.Message, StringComparison.Ordinal);
        Assert.Contains("does not assume which", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnAgreeingLabel_IsNotReported()
    {
        // Arrange & act.
        var problems = await Judge(
            "variable a", "variable b",
            "link a -> b +", "link b -> a -",
            "loop B1 \"self-correcting\" a b");

        // Assert.
        Assert.Empty(Of(problems, CausalLoopValidator.LabelDisagreesRuleId));
    }

    [Fact]
    public async Task ZeroNegativeLinksLabelledReinforcing_IsNotReported()
    {
        // Arrange & act.
        // Zero is even. A tool that got this wrong would report a finding against every new
        // document it creates, which is the case most likely to be seen and least likely to be
        // tested.
        var problems = await Judge(
            "variable a", "variable b",
            "link a -> b +", "link b -> a +",
            "loop R1 \"all positive\" a b");

        // Assert.
        Assert.Empty(Of(problems, CausalLoopValidator.LabelDisagreesRuleId));
    }

    /// <summary>The finding this notation most exists to produce.</summary>
    [Fact]
    public async Task ACycleNoLoopStatementNames_IsReported()
    {
        // Arrange & act.
        var problems = await Judge(
            "variable a", "variable b",
            "link a -> b +", "link b -> a +");

        // Assert.
        var finding = Assert.Single(Of(problems, CausalLoopValidator.UnlabelledLoopRuleId));
        Assert.Contains("a → b", finding.Message, StringComparison.Ordinal);
        Assert.Contains("reinforcing", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACycleTheDocumentNames_IsNotReportedAsUnlabelled_HoweverItIsRotated()
    {
        // Arrange & act.
        // The author began the loop at b; the enumerator begins it at a. Same loop.
        var problems = await Judge(
            "variable a", "variable b", "variable c",
            "link a -> b +", "link b -> c +", "link c -> a +",
            "loop R1 \"named from the middle\" b c a");

        // Assert.
        Assert.Empty(Of(problems, CausalLoopValidator.UnlabelledLoopRuleId));
    }

    [Fact]
    public async Task AnUndecidableLoop_SaysWhatWouldDecideIt()
    {
        // Arrange & act.
        var problems = await Judge(
            "variable a", "variable b",
            "link a -> b +", "link b -> a",
            "loop R1 \"unmarked somewhere\" a b");

        // Assert.
        var finding = Assert.Single(Of(problems, CausalLoopValidator.UndecidableLoopRuleId));
        Assert.Contains("no stated polarity", finding.Message, StringComparison.Ordinal);
        Assert.Contains("+ or -", finding.Message, StringComparison.Ordinal);
        // Undecidable is not a disagreement: nothing is claimed to be wrong with the label.
        Assert.Empty(Of(problems, CausalLoopValidator.LabelDisagreesRuleId));
    }

    [Fact]
    public async Task ALoopNamingAPathTheArrowsDoNotClose_IsReported()
    {
        // Arrange & act.
        var problems = await Judge(
            "variable a", "variable b",
            "link a -> b +",
            "loop R1 \"wishful\" a b");

        // Assert.
        var finding = Assert.Single(Of(problems, CausalLoopValidator.LoopIsNotACycleRuleId));
        Assert.Contains("do not close into a cycle", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ALinkToAnUndeclaredVariable_IsReportedAndNotDrawn()
    {
        // Arrange & act.
        var problems = await Judge("variable a", "link a -> nowhere +");

        // Assert.
        var finding = Assert.Single(Of(problems, CausalLoopValidator.DanglingLinkRuleId));
        Assert.Contains("'nowhere'", finding.Message, StringComparison.Ordinal);
        Assert.Contains("not drawn", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnreadableLine_IsReportedAtItsLine()
    {
        // Arrange & act.
        var problems = await Judge("causal-loop 1", "variable a", "nonsense");

        // Assert.
        var finding = Assert.Single(Of(problems, CausalLoopValidator.UnreadableRuleId));
        // One-based, as an editor counts.
        Assert.Equal(new DiagramProblemLineLocation(3), finding.Location);
    }

    [Fact]
    public async Task TheStarterDocument_JudgesClean()
    {
        // Arrange.
        // The first thing a user ever sees of this type must not be the tool complaining about
        // a file the tool itself just wrote.
        var text = new CausalLoopDocumentFactory(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin)
            .CreateEmptyDocument("feedback");

        // Act.
        var problems = await _validator.ValidateAsync(
            new DiagramValidationRequest(text, "feedback", @"C:\p", @"C:\p\feedback.cld", @"C:\p\feedback.adp"),
            TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task NothingIsEverRewritten()
    {
        // Arrange.
        var text = "variable a\r\nvariable b\r\nlink a -> b +\r\nlink b -> a -\r\nloop R1 \"wrong\" a b\r\n";
        var request = new DiagramValidationRequest(text, "f", @"C:\p", @"C:\p\f.cld", @"C:\p\f.adp");

        // Act.
        var problems = await _validator.ValidateAsync(request, TestContext.Current.CancellationToken);

        // Assert.
        // The disagreement is found, and the document it was found in is untouched - the
        // request carries the text by value, and nothing here writes a file at all.
        Assert.NotEmpty(problems);
        Assert.Equal(text, request.Document);
    }

    [Fact]
    public void TheValidatorIsRegistered_ForThisOrigin()
    {
        // Arrange.
        using var provider = new ServiceCollection().AddCausalLoop().BuildServiceProvider();

        // Act.
        var validators = provider.GetServices<IDiagramValidator>().ToArray();

        // Assert.
        Assert.NotEmpty(validators);
        Assert.Equal(
            ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin,
            Assert.Single(validators).Origin);
    }
}
