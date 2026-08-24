using EtAlii.Adp.Diagram;
using Xunit;

namespace EtAlii.Adp.C4.Tests;

/// <summary>
/// The empty document each C4 type starts from. An empty C4 model is not an empty file: every
/// view except the landscape is a view *of* something, so the document has to carry the thing
/// its view is scoped to or the view would not be legal DSL (c4-diagrams Requirement 2.1).
/// </summary>
public class C4DocumentFactoryTests
{
    private static string Create(C4ViewKind kind, string baseName = "payments") =>
        new C4DocumentFactory(new DiagramOrigin("c4", "test"), kind).CreateEmptyDocument(baseName);

    public static TheoryData<C4ViewKind> EveryViewKind() =>
    [
        C4ViewKind.SystemLandscape,
        C4ViewKind.SystemContext,
        C4ViewKind.Container,
        C4ViewKind.Component,
        C4ViewKind.Dynamic,
        C4ViewKind.Deployment,
    ];

    [Theory]
    [MemberData(nameof(EveryViewKind))]
    public void EveryKind_ProducesAWorkspaceWithAModelAndViews(C4ViewKind kind)
    {
        var document = Create(kind);

        Assert.StartsWith("workspace \"payments\" {", document, StringComparison.Ordinal);
        Assert.Contains("    model {", document, StringComparison.Ordinal);
        Assert.Contains("    views {", document, StringComparison.Ordinal);
        Assert.EndsWith("}\n", document, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(EveryViewKind))]
    public void EveryKind_ProducesBalancedBraces(C4ViewKind kind)
    {
        var document = Create(kind);

        Assert.Equal(document.Count(c => c == '{'), document.Count(c => c == '}'));
    }

    [Theory]
    [InlineData(C4ViewKind.SystemLandscape, "systemLandscape \"landscape\"")]
    [InlineData(C4ViewKind.SystemContext, "systemContext payments \"context\"")]
    [InlineData(C4ViewKind.Container, "container payments \"containers\"")]
    [InlineData(C4ViewKind.Component, "component payments.application \"components\"")]
    [InlineData(C4ViewKind.Dynamic, "dynamic payments \"scenario\"")]
    [InlineData(C4ViewKind.Deployment, "deployment payments \"Production\" \"deployment\"")]
    public void EachKind_DeclaresItsOwnViewScopedToWhatThatKindRequires(C4ViewKind kind, string expected)
    {
        Assert.Contains(expected, Create(kind), StringComparison.Ordinal);
    }

    [Fact]
    public void ALandscape_HasNoFocalSystem_WhichIsWhatDistinguishesItFromAContextDiagram()
    {
        // Requirement 9.6: a landscape is a context diagram without a focus. Scoping it to a
        // system would quietly turn it into one.
        Assert.DoesNotContain("systemLandscape payments", Create(C4ViewKind.SystemLandscape), StringComparison.Ordinal);
    }

    [Fact]
    public void AComponentView_ReachesOneLevelDeeper_BecauseItIsScopedToAContainer()
    {
        var document = Create(C4ViewKind.Component);

        Assert.Contains("container \"Application\"", document, StringComparison.Ordinal);
        // Requirement 10.3: a container without a technology is a violation, so the placeholder
        // does not create one the user would immediately be warned about... it names the slot.
        Assert.Contains("\"Technology\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeploymentView_GetsAnEnvironmentToBindTo()
    {
        Assert.Contains("deploymentEnvironment \"Production\"", Create(C4ViewKind.Deployment), StringComparison.Ordinal);
    }

    [Fact]
    public void ADynamicView_StartsWithNoInteractions_RatherThanIncludingEverything()
    {
        // "include *" means something different on a dynamic view: its body is its ordered
        // interactions, and a new one has none yet (Requirement 7.5).
        var document = Create(C4ViewKind.Dynamic);

        Assert.Contains("dynamic payments \"scenario\"", document, StringComparison.Ordinal);
        Assert.DoesNotContain("include *", document, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("my map", "my_map")]
    [InlineData("2024-review", "_2024_review")]
    [InlineData("payments!", "payments")]
    [InlineData("", "system")]
    [InlineData("   ", "system")]
    [InlineData("***", "system")]
    public void AFileName_BecomesAValidDslIdentifier(string baseName, string expectedIdentifier)
    {
        var document = Create(C4ViewKind.SystemContext, baseName);

        Assert.Contains($"        {expectedIdentifier} = softwareSystem ", document, StringComparison.Ordinal);
    }

    [Fact]
    public void ANameWithAQuote_IsEscapedRatherThanBreakingTheDocument()
    {
        var document = Create(C4ViewKind.SystemContext, "the \"good\" one");

        Assert.Contains("\\\"good\\\"", document, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCodeFactory_RefusesAndSaysWhy()
    {
        var factory = new C4CodeDocumentFactory();

        var exception = Assert.Throws<NotSupportedException>(() => factory.CreateEmptyDocument("anything"));
        Assert.Equal(C4CodeDocumentFactory.Unavailable, exception.Message);
        Assert.Equal("c4/code", factory.Origin.Key);
    }
}
