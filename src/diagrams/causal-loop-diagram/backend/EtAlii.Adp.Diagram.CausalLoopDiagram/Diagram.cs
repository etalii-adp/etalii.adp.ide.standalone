using EtAlii.Adp.Documents;
namespace EtAlii.Adp.Diagram.CausalLoopDiagram;

/// <summary>
/// The causal loop diagram, cataloged in docs/tools.md as <c>systems/causal-loop-diagram</c>.
/// </summary>
/// <remarks>
/// <para>
/// The entry notation of system dynamics: variables joined by causal links, each link carrying a
/// polarity, and the cycles those links form labelled reinforcing or balancing. It is the one
/// diagram type in this catalog whose central claim is about <i>cycles</i> rather than about
/// hierarchy, containment or sequence, which is why the module's first component is a cycle
/// enumerator rather than a layout.
/// </para>
/// <para>
/// <b>The origin's spelling is deliberate on two points</b>, both confirmed by the user, and
/// neither is to be tidied. It is <c>causal</c> and not <c>casual</c>. And it keeps the
/// <c>-diagram</c> suffix although no other origin in the catalog carries one on its type
/// segment - the one origin whose type is literally <c>diagram</c> is <c>d2/diagram</c>, where
/// <c>d2</c> is the vendor. <c>systems/causal-loop</c> would have matched the pattern and was
/// not chosen. An <c>.adp</c> names the origin on its first line, so changing it later would
/// have to reach every document already carrying it.
/// </para>
/// <para>
/// <c>.cld</c> means a causal loop diagram everywhere it appears, so a bare body routes here on
/// sight - unlike <c>.yml</c>, which a repository is full of. That makes
/// <see cref="DiagramDefinition.SharedExtension"/> false, which is the default.
/// </para>
/// </remarks>
public static class Diagram
{
    /// <summary>The body extension, in one place so nothing has to spell it twice.</summary>
    public const string DocumentExtension = ".cld";

    /// <summary>Whether a path is one of this module's bodies.</summary>
    public static bool IsBody(string path) =>
        path is { Length: > 0 } &&
        path.EndsWith(DocumentExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>The causal loop diagram: what feeds back on what, and which cycles that makes.</summary>
    public static DiagramDefinition CausalLoop { get; } = new(
        ServiceCollectionAddCausalLoopExtension.CausalLoopOrigin,
        "Causal loop diagram",
        "How the parts of a system feed back on each other: variables joined by polarised causal links, with the loops they form identified as reinforcing or balancing.",
        Icon: "mdi-sync-circle",
        Extension: DocumentExtension,
        Build: builder => builder.Services.AddCausalLoop());

    /// <summary>What discovery reads.</summary>
    public static DiagramDefinition[] Definitions { get; } =
    [
        CausalLoop,
    ];
}
