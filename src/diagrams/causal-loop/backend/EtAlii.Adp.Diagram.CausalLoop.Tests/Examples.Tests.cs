using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The shipped examples (causal-loop-diagram Requirement 11), held to what their readmes claim.
/// </summary>
/// <remarks>
/// <para>
/// These are not fixtures. The fixtures elsewhere in this project pin parser behaviour one
/// construct at a time; these are documents a reader opens. What is tested here is only that the
/// claims made about them stay true — that the reference example really does carry a label
/// disagreement, that the on-call example really is free of one — because a readme saying so is
/// exactly the kind of statement that rots.
/// </para>
/// <para>
/// <b>Authored for this repository, and labelled as authored.</b> Five candidate sources were
/// checked and all five rejected on licence grounds: nocomplexity/causalloopdiagram is GPL-3.0,
/// elbazjosh/AutoCLD carries no licence at all, Wikipedia's figures are CC BY-SA, MetaSD offers
/// only per-model author permission on stock-and-flow models, and bear96/System-Dynamics-Bot —
/// found in a fresh search for this task, and the only candidate carrying an actual corpus of
/// causal loop diagrams — is CC BY-NC-4.0, whose NonCommercial term fails the same test the
/// share-alike ones did. Nothing here is attributed to any of them.
/// </para>
/// </remarks>
public class ExamplesTests
{
    private static string ExamplesRoot
    {
        get
        {
            // Up out of bin/Debug/net10.0 and across to the module's examples folder.
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(IoPath.Combine(directory.FullName, "examples")))
            {
                directory = directory.Parent;
            }

            Assert.SkipWhen(directory is null, "The module's examples folder was not found from the test output.");
            return IoPath.Combine(directory.FullName, "examples");
        }
    }

    private static CausalLoopModel Load(string name)
    {
        var path = IoPath.Combine(ExamplesRoot, name, $"{name}.cld");
        Assert.True(File.Exists(path), $"The example '{name}' is missing: {path}");

        var result = CausalLoopParser.Parse(CausalLoopDocument.Parse(File.ReadAllText(path)));
        Assert.Empty(result.Problems);
        return result.Model;
    }

    private static async Task<IReadOnlyList<DiagramProblem>> Judge(string name)
    {
        var path = IoPath.Combine(ExamplesRoot, name, $"{name}.cld");
        var validator = new CausalLoopValidator(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin);

        return await validator.ValidateAsync(
            new DiagramValidationRequest(
                await File.ReadAllTextAsync(path),
                name,
                ExamplesRoot,
                path,
                IoPath.Combine(ExamplesRoot, name, $"{name}.adp")),
            TestContext.Current.CancellationToken);
    }

    private static IReadOnlyList<string> Names() =>
        [.. Directory.EnumerateDirectories(ExamplesRoot).Select(IoPath.GetFileName)!];

    // ---- every example opens with no setup -----------------------------------------------------

    /// <summary>
    /// Requirement 11.4: each example sits beside its own registration and opens from the
    /// explorer without anything being configured first.
    /// </summary>
    [Theory]
    [InlineData("reference")]
    [InlineData("on-call")]
    public void AnExample_SitsBesideItsOwnRegistration(string name)
    {
        // Act.
        var folder = IoPath.Combine(ExamplesRoot, name);
        var adp = IoPath.Combine(folder, $"{name}.adp");

        // Assert.
        Assert.True(File.Exists(adp), $"The registration for '{name}' is missing: {adp}");
        Assert.True(File.Exists(IoPath.Combine(folder, $"{name}.cld")));

        // The origin on the first line, which is what routes the pair.
        Assert.StartsWith(
            "systems/causal-loop-diagram",
            File.ReadAllText(adp),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryExampleFolder_IsOneOfTheOnesThisTestKnowsAbout()
    {
        // Act & assert.
        // So that an example added without a test is a failure rather than a silent gap.
        Assert.Equal(["on-call", "reference"], [.. Names().OrderBy(name => name, StringComparer.Ordinal)]);
    }

    [Theory]
    [InlineData("reference")]
    [InlineData("on-call")]
    public void AnExample_Parses_AndEveryLinkAndLoopNamesADeclaredVariable(string name)
    {
        // Act.
        var model = Load(name);

        // Assert.
        Assert.NotEmpty(model.Variables);
        Assert.NotEmpty(model.Links);
        Assert.NotEmpty(model.Loops);

        Assert.All(model.Links, link => Assert.True(
            model.Declares(link.From) && model.Declares(link.To),
            $"'{name}' has a link between {link.From} and {link.To}, one of which is undeclared."));

        Assert.All(model.Loops, loop => Assert.All(loop.Variables, member => Assert.True(
            model.Declares(member), $"'{name}' has loop {loop.Identifier} running through undeclared '{member}'.")));
    }

    // ---- the hard cases, together, in one shipped file ------------------------------------------

    /// <summary>
    /// The task's requirement on the corpus: one example exercises a reinforcing loop, a
    /// balancing loop, a delayed link, a variable in more than one loop, and a loop whose stated
    /// label disagrees with its computed polarity — so the group 1 finding is demonstrable from a
    /// shipped file rather than only from a test fixture.
    /// </summary>
    [Fact]
    public void TheReferenceExample_CarriesAllFiveHardCasesTogether()
    {
        // Arrange.
        var model = Load("reference");
        var polarity = model.Loops.ToDictionary(
            loop => loop.Identifier,
            loop => LoopPolarity.Of(model, loop.Variables),
            StringComparer.Ordinal);

        // Assert.
        // 1. A loop the arithmetic makes reinforcing.
        Assert.Contains(polarity, entry => entry.Value == LoopPolarityResult.Reinforcing);

        // 2. A loop the arithmetic makes balancing.
        Assert.Contains(polarity, entry => entry.Value == LoopPolarityResult.Balancing);

        // 3. A delayed link.
        Assert.Contains(model.Links, link => link.Delayed);

        // 4. A variable in more than one loop.
        Assert.Contains(
            model.Variables,
            variable => model.Loops.Count(loop => loop.Variables.Contains(variable.Id, StringComparer.Ordinal)) > 1);

        // 5. A stated label that disagrees with the computed polarity.
        Assert.Contains(model.Loops, loop =>
            loop.ClaimsReinforcing is { } claimed
            && polarity[loop.Identifier] is LoopPolarityResult.Reinforcing or LoopPolarityResult.Balancing
            && claimed != (polarity[loop.Identifier] == LoopPolarityResult.Reinforcing));
    }

    /// <summary>
    /// The disagreement is a demonstration, not a mistake, so it is pinned to the loop the readme
    /// names. A future edit that quietly fixed R3 would leave the readme describing a finding the
    /// file no longer produces.
    /// </summary>
    [Fact]
    public async Task TheReferenceExample_DisagreesOnR3Specifically_AndTheValidatorSaysSo()
    {
        // Arrange.
        var model = Load("reference");
        var r3 = Assert.Single(model.Loops, loop => loop.Identifier == "R3");

        // Act.
        var findings = await Judge("reference");

        // Assert.
        Assert.Equal(LoopPolarityResult.Balancing, LoopPolarity.Of(model, r3.Variables));
        Assert.True(r3.ClaimsReinforcing);

        var disagreement = Assert.Single(
            findings,
            finding => finding.RuleId == CausalLoopValidator.LabelDisagreesRuleId);
        Assert.Contains("R3", disagreement.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// "Unknown" is not "none". A link with no stated polarity makes the loop through it
    /// undecidable, and the reference example ships one so a reader can see the difference
    /// between a loop that balances and a loop nobody can count.
    /// </summary>
    [Fact]
    public void TheReferenceExample_ShipsAnUndecidableLoop()
    {
        // Arrange.
        var model = Load("reference");

        // Assert.
        Assert.Contains(model.Links, link => link.Polarity == CausalLoopPolarity.Unstated);
        Assert.Contains(
            model.Loops,
            loop => LoopPolarity.Of(model, loop.Variables) == LoopPolarityResult.Undecidable);
    }

    /// <summary>
    /// The other example is the control. Every label in it agrees with its arrows, so a reader
    /// meeting the reference example's finding knows it is deliberate rather than a house style.
    /// </summary>
    [Fact]
    public async Task TheOnCallExample_HasNoDisagreementAtAll()
    {
        // Arrange.
        var model = Load("on-call");

        // Act.
        var findings = await Judge("on-call");

        // Assert.
        Assert.NotEmpty(model.Loops);
        Assert.DoesNotContain(
            findings, finding => finding.RuleId == CausalLoopValidator.LabelDisagreesRuleId);

        Assert.All(model.Loops, loop =>
        {
            var computed = LoopPolarity.Of(model, loop.Variables);
            Assert.True(
                loop.ClaimsReinforcing == (computed == LoopPolarityResult.Reinforcing),
                $"{loop.Identifier} claims {(loop.ClaimsReinforcing == true ? "reinforcing" : "balancing")} but counts {computed}.");
        });
    }

    // ---- the layout the reader will actually see ------------------------------------------------

    /// <summary>
    /// Requirement 6.6 asked for the layout to be measured on real documents rather than on
    /// synthetic fixtures. Until this task there were none; these are they, so the non-overlap
    /// guard is run over them here.
    /// </summary>
    [Theory]
    [InlineData("reference")]
    [InlineData("on-call")]
    public void AnExample_ArrangesWithoutOverlap(string name)
    {
        // Act.
        var result = SelfOrganizingLayout.Compute(Load(name));

        // Assert.
        Assert.True(result.IsArranged, result.Refusal);

        var boxes = result.Boxes.Values.ToArray();
        Assert.NotEmpty(boxes);

        for (var first = 0; first < boxes.Length; first++)
        {
            for (var second = first + 1; second < boxes.Length; second++)
            {
                Assert.False(
                    boxes[first].Overlaps(boxes[second]),
                    $"'{name}' arranges with an overlap: {boxes[first]} and {boxes[second]}.");
            }
        }
    }
}
