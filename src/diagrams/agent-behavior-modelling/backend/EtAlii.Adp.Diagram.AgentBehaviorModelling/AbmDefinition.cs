using EtAlii.Adp.Specification.Disl;

namespace EtAlii.Adp.Diagram.AgentBehaviorModelling;

/// <summary>
/// The behavior model's bundled DISL definition (<c>definition/agent-behavior-modelling.dis</c>, from
/// etalii-adp/etalii.adp), loaded once, with the wire ids of its <c>x-abm</c> block.
/// </summary>
/// <remarks>
/// <b>A node's DISL type is the one <c>x-abm.types</c> maps to its kind</b>: <c>Sequence</c> for
/// <c>sequence</c>, <c>Do</c> for <c>action</c>, and so on for the eleven. The persistence plugin reads
/// and writes the types through these two maps, so the Markdown's keywords stay the module's.
/// </remarks>
internal static class AbmDefinition
{
    private static readonly Lazy<BundledDefinition> Loaded = new(() => BundledDefinition.Load(typeof(AbmDefinition).Assembly, "agent-behavior-modelling.dis"));

    private static readonly Lazy<WireIdMap> LoadedIds = new(() => WireIdMap.Of(Specification, "x-abm"));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedKindOfType = new(() =>
        Ids.Types.Where(pair => AbmNodeKinds.IsKnown(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    private static readonly Lazy<IReadOnlyDictionary<string, string>> LoadedTypeOfKind = new(() =>
        KindOfType.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal));

    /// <summary>The definition.</summary>
    public static DislSpecification Specification => Loaded.Value.Specification;

    /// <summary>The wire ids of <c>x-abm</c>.</summary>
    public static WireIdMap Ids => LoadedIds.Value;

    /// <summary>The kind of each node type: <c>Do</c> is <c>action</c>.</summary>
    public static IReadOnlyDictionary<string, string> KindOfType => LoadedKindOfType.Value;

    /// <summary>The node type of each kind: <c>action</c> is <c>Do</c>.</summary>
    public static IReadOnlyDictionary<string, string> TypeOfKind => LoadedTypeOfKind.Value;
}
