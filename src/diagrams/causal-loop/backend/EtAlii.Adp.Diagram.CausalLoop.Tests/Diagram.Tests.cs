using EtAlii.Adp.Backend.Diagrams;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

/// <summary>
/// The definition discovery reads, and the document factory core refuses to start without
/// (causal-loop-diagram Requirements 1.1, 1.4, 5.1, 5.2).
/// </summary>
public class DiagramTests
{
    [Fact]
    public void TheDefinition_CarriesTheOriginAndExtensionTheSpecificationFixed()
    {
        // Arrange & act & assert.
        var definition = Diagram.CausalLoop;

        // Both halves of the origin are deliberate and were confirmed rather than assumed:
        // `causal` and not `casual`, and the `-diagram` suffix no other origin carries.
        Assert.Equal("systems", definition.Origin.Vendor);
        Assert.Equal("causal-loop-diagram", definition.Origin.Type);
        Assert.Equal(".cld", definition.Extension);
    }

    [Fact]
    public void TheExtension_IsClaimedOnSight_BecauseCldMeansOnlyThis()
    {
        // Arrange & act & assert.
        // Unlike `.yml`, which a repository is full of, a `.cld` is a causal loop diagram
        // wherever it appears - so a bare body routes here without the user registering it.
        Assert.False(Diagram.CausalLoop.SharedExtension);
        Assert.True(Diagram.CausalLoop.RoutesBareBody);
        Assert.True(Diagram.CausalLoop.HasDocumentSibling);
    }

    [Fact]
    public void TheDefinition_IntroducesItself_SoTheAddDialogCanOfferIt()
    {
        // Arrange & act & assert.
        Assert.NotEqual("", Diagram.CausalLoop.Title);
        Assert.NotEqual("", Diagram.CausalLoop.Description);
        Assert.NotEqual("", Diagram.CausalLoop.Icon);
    }

    [Fact]
    public void OneDefinition_AndDiscoveryCanSeeIt()
    {
        // Arrange & act & assert.
        // The floor first: an empty Definitions array satisfies every claim below vacuously.
        Assert.NotEmpty(Diagram.Definitions);
        Assert.Same(Diagram.CausalLoop, Assert.Single(Diagram.Definitions));
    }

    /// <summary>
    /// The registration this module could not defer. A definition declaring an extension with no
    /// factory is a startup error naming the type, so the host does not boot at all - which is
    /// why the specification made it a requirement rather than an implementation detail.
    /// </summary>
    [Fact]
    public void AddCausalLoop_RegistersTheDocumentFactory_WithoutWhichTheHostWouldNotStart()
    {
        // Arrange.
        using var provider = new ServiceCollection().AddCausalLoop().BuildServiceProvider();

        // Act.
        var factory = Assert.Single(provider.GetServices<IDiagramDocumentFactory>());

        // Assert.
        Assert.Equal(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin, factory.Origin);
    }

    [Fact]
    public void TheStarterDocument_IsALoop_RatherThanAnEmptyFile()
    {
        // Arrange.
        var factory = new CausalLoopDocumentFactory(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin);

        // Act.
        var text = factory.CreateEmptyDocument("feedback");

        // Assert.
        // A new file should show what the type is for on first open: two variables feeding each
        // other positively are the smallest honest example of the notation's whole point.
        Assert.Contains("variable population", text, StringComparison.Ordinal);
        Assert.Contains("link population -> births +", text, StringComparison.Ordinal);
        Assert.Contains("link births -> population +", text, StringComparison.Ordinal);
        Assert.Contains("loop R1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStarterDocument_LabelsItsLoopCorrectly_SoTheToolDoesNotShipAFinding()
    {
        // Arrange.
        var factory = new CausalLoopDocumentFactory(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin);

        // Act.
        var text = factory.CreateEmptyDocument("feedback");
        var negativeLinks = text
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
            .Count(line => line.StartsWith("link ", StringComparison.Ordinal) && line.EndsWith(" -", StringComparison.Ordinal));

        // Assert.
        // The rule this whole module exists to apply: a loop is reinforcing when its count of
        // negative links is even, and zero is even. The starter says R1, so it had better have
        // an even count - otherwise the first thing a user meets is the tool contradicting
        // itself about a file it just wrote.
        Assert.Equal(0, negativeLinks);
        Assert.Equal(0, negativeLinks % 2);
        Assert.Contains("loop R1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStarterDocument_UsesTheHouseLineEnding()
    {
        // Arrange & act.
        var text = new CausalLoopDocumentFactory(ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin)
            .CreateEmptyDocument("feedback");

        // Assert.
        // A created file has no existing style to preserve, so it takes the house one.
        Assert.EndsWith("\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(text.Replace("\r\n", "", StringComparison.Ordinal), "\n", StringComparison.Ordinal);
    }
}
