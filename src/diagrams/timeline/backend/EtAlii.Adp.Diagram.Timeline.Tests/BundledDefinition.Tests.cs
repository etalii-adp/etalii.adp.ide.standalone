using System.Reflection;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads with the module's plugin functions.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "timeline";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("timeline", Diagram.Timeline.Origin.Key, "net.etalii.adp.generic.timeline")];

    /// <summary>The definition as the module holds it, loaded with its plugin functions.</summary>
    protected override BundledDefinition Load(BundledDefinitionExpectation expected) =>
        BundledDefinition.Load(Module, expected.Name + ".dis", TimelineTimes.Plugins());

    /// <summary>What the runtime said as the module loaded its definition, which the module's providers derive from.</summary>
    protected override IReadOnlyList<LoadDiagnostic> DiagnosticsOf(BundledDefinition loaded) => TimelineDefinition.Diagnostics;

    /// <summary>
    /// A time is read with the module's own plugin functions, not the definition's fallbacks: those
    /// read ISO 8601 only, where the parser also reads <c>July 5, 2026</c>.
    /// </summary>
    [Theory]
    [InlineData("readableTime('July 5, 2026')", true)]
    [InlineData("readableTime(' 2026-07-05 ')", true)]
    [InlineData("readableTime('2026-02-30')", false)]
    [InlineData("compareTimes('2026-07-05T00:00:00+02:00', '2026-07-04T23:00:00')", -1L)]
    [InlineData("dateAfter('2026-01-05T22:00:00', 6)", "2026-01-11")]
    public void TheModulesPluginFunctions_AreTheOnesCalled(string expression, object expected)
    {
        var value = TimelineDefinition.Specification.Environment(DislContext.Element).Compile(expression).Evaluate(new Dictionary<string, object?>());

        Assert.Equal(expected, value);
    }
}
