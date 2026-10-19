using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using Xunit;

namespace EtAlii.Adp.Diagram.Timeline.Tests.Parity;

/// <summary>
/// The cross-check of the FBL implementation's Requirement 11.6 for the timeline: the ids per type the vendored example
/// binding reads from every <c>.tml</c> file of the repository are the ones the module reads. It moved here from
/// <c>EtAlii.Adp.Specification.Fbl.Tests</c> so that no core project references a tool module (code-structure T287);
/// its divergences are still listed in that project's <c>RealFiles/divergences.json</c>.
/// </summary>
public class TimelineCrossCheckTests
{
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("timeline");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheIdsTheModuleReads(string file) =>
        ModuleCrossCheck.Check("timeline", file, element => element.IsRelation || element.IdIsStored ? element.Id : null, Module);

    /// <summary>
    /// The module has no parser of its own since timeline-disl-fbl task 5: its side is what its body reads through the
    /// module's own binding, after its stand-in has asked whether the body is YAML at all. A body YamlDotNet refuses,
    /// such as one with a key written twice, is one the module reads nothing of: it reports timeline.unparseable. The
    /// vendored example may still read it, which is then a listed divergence. An entry is named by the id it is written
    /// with, the module binding's storedId, as the module finds it.
    /// </summary>
    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        if (TimelineYamlSyntax.Check(text) is not null) return ids;
        foreach (var element in TimelineBody.Parse(text).Model.Elements)
        {
            if (element.Attributes.GetValueOrDefault("storedId") is string { Length: > 0 } stored) ModuleCrossCheck.Add(ids, element.Type, stored);
        }
        return ids;
    }
}
