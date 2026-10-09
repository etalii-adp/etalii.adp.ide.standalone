using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace EtAlii.Adp.Diagram.AgentActivityDiagram.Tests;

/// <summary>
/// The definition this module derives from is the one etalii.adp published, byte for byte, and
/// this host can read it (agent-activity-diagram Requirement 10.2).
/// </summary>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    private static byte[] Resource(string logicalName)
    {
        using var stream = _module.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"The module embeds no resource {logicalName}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static JsonElement Json(string logicalName) => JsonDocument.Parse(Resource(logicalName)).RootElement;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    [Fact]
    public void TheBundledDefinition_HashesToItsProvenance()
    {
        // Seen to fail against one byte changed in the bundled `.dis`.
        var provenance = Json("agent-activity-diagram.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(Resource("agent-activity-diagram.dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    [Fact]
    public void TheBundledBinding_HashesToItsProvenance()
    {
        var provenance = Json("agent-activity-diagram.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(Resource("agent-activity-diagram.fbl")));

        Assert.Equal("definitions/diagrams/agent-activity-diagram.fbl", provenance.GetProperty("binding").GetString());
        Assert.Equal(provenance.GetProperty("bindingSha256").GetString(), actual);
    }

    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = Json("agent-activity-diagram.provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/agent-activity-diagram.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = Json("agent-activity-diagram.dis").GetProperty("language");

        Assert.Equal("net.etalii.adp.etalii.agent-activity-diagram", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.AgentActivity.Origin.Key, language.GetProperty("origin").GetString());
        Assert.Equal(Diagram.DocumentExtension, "." + language.GetProperty("fileExtension").GetString());
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
