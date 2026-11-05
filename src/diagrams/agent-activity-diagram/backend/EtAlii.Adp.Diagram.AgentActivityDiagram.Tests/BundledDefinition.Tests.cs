using System.Reflection;
using System.Security.Cryptography;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// The definition this module derives from is the one etalii.adp published, byte for byte, and
/// this host can read it (agent-activity-diagram Requirement 10.2).
/// </summary>
public sealed class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "agent-activity-diagram";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("agent-activity-diagram", Diagram.AgentActivity.Origin.Key, "net.etalii.adp.etalii.agent-activity-diagram") { Extension = Diagram.DocumentExtension },
    ];

    /// <summary>The definition is DISL 0.4, of which this runtime warns; it had no load case before the contract, and a warning is allowed.</summary>
    protected override bool AWarningIsAllowed => true;

    [Fact]
    public void TheBundledBinding_HashesToItsProvenance()
    {
        var provenance = Module.ResourceJson("agent-activity-diagram.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(Module.ResourceBytes("agent-activity-diagram.fbl")));

        Assert.Equal("definitions/diagrams/agent-activity-diagram.fbl", provenance.GetProperty("binding").GetString());
        Assert.Equal(provenance.GetProperty("bindingSha256").GetString(), actual);
    }

    [Fact]
    public void TheBundledDefinition_LoadsInThisHost_AndSaysWhatTheModuleReadsOffIt()
    {
        // The definition's lists say things per item (`item.title`, `item.link`), which this host's
        // reader walked in the wrong context until this module needed it: a definition that does
        // not load fails here, before anything is drawn from it.
        Assert.Equal(["progressing", "pending", "inputRequired", "finished"], AadDefinition.TaskStatus.Members.Select(member => member.Name));
        Assert.Equal("input-required", AadDefinition.TaskStatus.Find("inputRequired")!.Stored);
        Assert.Equal("Input Required", AadDefinition.SpecificationStatus.Find("input-required")!.Label);
        Assert.Equal("Local machine", AadDefinition.EnvironmentKind.Find("local-machine")!.Label);

        // Progressing and Input Required start unfolded; Pending, Finished and a location's pull
        // requests start folded (Requirements 4.4 and 4.5).
        Assert.Equal(["finished", "pending", AadDefinition.PullRequestsGroup], AadDefinition.CollapsedByDefault.Order(StringComparer.Ordinal));
    }
}
