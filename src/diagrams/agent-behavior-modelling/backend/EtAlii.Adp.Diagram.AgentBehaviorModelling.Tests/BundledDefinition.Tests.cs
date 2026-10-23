using System.Reflection;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is.
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "agent-behavior-modelling";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
        [new("agent-behavior-modelling", Diagram.AgentBehaviorModelling.Origin.Key, "net.etalii.adp.etalii.agent-behavior-modelling")];
}
