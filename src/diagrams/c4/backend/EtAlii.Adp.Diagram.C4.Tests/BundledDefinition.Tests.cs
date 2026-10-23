using System.Reflection;
using System.Text.Json.Nodes;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The six DISL definitions bundled from etalii-adp/etalii.adp are the bytes their provenance says they
/// are, the bytes in <c>definition/</c>, and they load with no diagnostic of severity error; the layers
/// they share are equal; and <c>c4/code</c> has none (c4-disl-fbl Requirements 2.3, 2.5 to 2.7).
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionSetContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "c4";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("c4-system-landscape", Diagram.SystemLandscape.Origin.Key, "net.etalii.adp.c4.system-landscape"),
        new("c4-context", Diagram.SystemContext.Origin.Key, "net.etalii.adp.c4.context"),
        new("c4-container", Diagram.Container.Origin.Key, "net.etalii.adp.c4.container"),
        new("c4-component", Diagram.Component.Origin.Key, "net.etalii.adp.c4.component"),
        new("c4-dynamic", Diagram.Dynamic.Origin.Key, "net.etalii.adp.c4.dynamic"),
        new("c4-deployment", Diagram.Deployment.Origin.Key, "net.etalii.adp.c4.deployment"),
    ];

    /// <summary>The definitions and their origins.</summary>
    public static TheoryData<string, string> Origins { get; } = new(Definitions.Select(definition => (definition.Name, definition.Origin)));

    /// <summary>The definitions are DISL 0.5, of which the runtime warns (Requirement 2.6 asks for no error).</summary>
    protected override bool AWarningIsAllowed => true;

    /// <summary>The module holds the six as one set.</summary>
    protected override IReadOnlyCollection<BundledDefinition> Held => C4Definitions.Set.Definitions;

    /// <summary>The layers the six share, which Requirement 2.3 holds equal in all six (Q8).</summary>
    protected override IReadOnlyList<(string Part, Func<JsonNode, JsonNode?> Of)> RepeatedParts { get; } =
        [.. new[] { "metamodel", "notation", "forms", "constraints", "behavior", "persistence" }.Select(layer => ($"{layer} layer", (Func<JsonNode, JsonNode?>)(definition => definition[layer])))];

    /// <summary>A definition as the module holds it: one of its set, by its upstream path.</summary>
    protected override BundledDefinition Load(BundledDefinitionExpectation expected) =>
        Assert.Single(C4Definitions.Set.Definitions, candidate => candidate.Provenance.Path == "definitions/diagrams/" + expected.Name + ".dis");

    /// <summary>What the module reports of a definition, by its upstream path.</summary>
    protected override IReadOnlyList<LoadDiagnostic> DiagnosticsOf(BundledDefinition loaded) =>
        [.. C4Definitions.Diagnostics.Where(entry => entry.Path == loaded.Provenance.Path).Select(entry => entry.Diagnostic)];

    /// <summary>Each definition declares the catalog origin of the type it is found by, and is the one found by that type's view kind.</summary>
    [Theory]
    [MemberData(nameof(Origins))]
    public void TheBundledDefinition_IsFoundByItsTypesOriginAndViewKind(string name, string origin)
    {
        var type = Assert.Single(Diagram.Definitions, definition => definition.Origin.Key == origin);
        (ToolOrigin _, C4ViewKind viewKind, string resource) = Assert.Single(C4Definitions.Types, candidate => candidate.Origin == type.Origin);

        Assert.Equal(name + ".dis", resource);
        Assert.Equal(origin, C4Definitions.For(type.Origin).Specification.Origin);
        Assert.Same(C4Definitions.For(type.Origin), C4Definitions.For(viewKind));
        Assert.Equal("definitions/diagrams/" + name + ".dis", C4Definitions.For(type.Origin).Provenance.Path);
    }

    /// <summary>
    /// <c>c4/code</c> keeps its registration and its refusing empty document, and has no definition
    /// (Requirement 2.7).
    /// </summary>
    [Fact]
    public void TheCodeType_HasNoDefinitionAndStillRefuses()
    {
        var services = new ServiceCollection();
        services.AddC4();
        using var provider = services.BuildServiceProvider();
        var factory = new DiagramDocumentFactories(provider.GetServices<IDiagramDocumentFactory>()).Find(Diagram.Code.Origin);

        Assert.Null(C4Definitions.Find(Diagram.Code.Origin));
        Assert.Throws<KeyNotFoundException>(() => C4Definitions.For(Diagram.Code.Origin));
        Assert.DoesNotContain(C4Definitions.Types, type => type.Origin == Diagram.Code.Origin);
        Assert.NotNull(factory);
        var exception = Assert.Throws<NotSupportedException>(() => factory.CreateEmptyDocument("anything"));
        Assert.Equal(ServiceCollectionAddC4Extension.CodeUnavailable, exception.Message);
    }
}
