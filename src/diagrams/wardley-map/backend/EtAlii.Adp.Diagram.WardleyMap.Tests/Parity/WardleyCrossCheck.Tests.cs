using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Diagram.WardleyMap.Tests.Parity;

/// <summary>
/// The cross-check of the FBL implementation's Requirement 11.6 for the Wardley map (wardley-map-disl-fbl task 11): the
/// kind and key the module's binding gives every element of every <c>.owm</c> file of the repository (its <c>sidecar</c>
/// key, FBL 0.12 section 5.3) are the keys the module keeps its identities under (<see cref="WardleyIdentityKeys"/>), for
/// what the module reads. Its divergences are listed in <c>EtAlii.Adp.Specification.Fbl.Tests</c>'s
/// <c>RealFiles/divergences.json</c>.
/// </summary>
public class WardleyCrossCheckTests
{
    public static TheoryData<string> Files() => ModuleCrossCheck.Files("wardley-map");

    [Theory]
    [MemberData(nameof(Files))]
    public void TheBindingReadsTheKeysTheModuleKeeps(string file) =>
        ModuleCrossCheck.Check("wardley-map", file, element => element.IdentityKind is { } kind ? $"{kind}:{element.IdentityKey}" : null, Module);

    /// <summary>
    /// The module's side: the identity of every element it reads, its kind and its key as the store keeps it, later holders
    /// of one key numbered (<see cref="WardleyIdentityKeys.Holders"/>), under the binding's type for it.
    /// </summary>
    private static IReadOnlyDictionary<string, SortedSet<string>> Module(string text)
    {
        var ids = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        var map = WardleyBody.Read(LineDocument.Parse(text)).Model;
        foreach ((string key, WardleyComponent component) in WardleyIdentityKeys.Holders(map.Components, WardleyIdentityKeys.Of))
        {
            ModuleCrossCheck.Add(ids, component.Kind.ToString(), $"{WardleyIdentityKind.Component}:{key}");
        }
        foreach ((string key, WardleyLink link) in WardleyIdentityKeys.Holders(map.Links, WardleyIdentityKeys.Of))
        {
            ModuleCrossCheck.Add(ids, link.Kind.ToString(), $"{WardleyIdentityKind.Link}:{key}");
        }
        foreach ((string key, WardleyPipeline pipeline) in WardleyIdentityKeys.Holders(map.Pipelines, WardleyIdentityKeys.Of))
        {
            ModuleCrossCheck.Add(ids, pipeline.Form == WardleyPipelineForm.Legacy ? "LegacyPipeline" : "Pipeline", $"{WardleyIdentityKind.Pipeline}:{key}");
        }
        foreach ((string key, _) in WardleyIdentityKeys.Holders(map.Pipelines.SelectMany(pipeline => pipeline.Children.Select(child => (pipeline, child))), item => WardleyIdentityKeys.Of(item.pipeline, item.child)))
        {
            ModuleCrossCheck.Add(ids, "PipelineComponent", $"{WardleyIdentityKind.PipelineChild}:{key}");
        }
        foreach ((string key, _) in WardleyIdentityKeys.Holders(map.Notes, WardleyIdentityKeys.Of)) ModuleCrossCheck.Add(ids, "Note", $"{WardleyIdentityKind.Note}:{key}");
        foreach ((string key, _) in WardleyIdentityKeys.Holders(map.Annotations, WardleyIdentityKeys.Of)) ModuleCrossCheck.Add(ids, "Annotation", $"{WardleyIdentityKind.Annotation}:{key}");
        foreach ((string key, _) in WardleyIdentityKeys.Holders(map.Attitudes, WardleyIdentityKeys.Of)) ModuleCrossCheck.Add(ids, "Attitude", $"{WardleyIdentityKind.Attitude}:{key}");
        foreach ((string key, _) in WardleyIdentityKeys.Holders(map.Accelerators, WardleyIdentityKeys.Of)) ModuleCrossCheck.Add(ids, "Accelerator", $"{WardleyIdentityKind.Accelerator}:{key}");
        return ids;
    }
}
