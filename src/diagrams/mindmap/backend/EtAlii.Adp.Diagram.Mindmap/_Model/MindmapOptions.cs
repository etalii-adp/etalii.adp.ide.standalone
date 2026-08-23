namespace EtAlii.Adp.Diagram.Mindmap;

/// <summary>
/// What appsettings.json may say about the mindmap module, under the <c>Mindmap</c> section.
/// Bound by the host and folded into <see cref="MindmapMetrics"/> - the one place layout
/// numbers live - rather than read anywhere else.
/// </summary>
public sealed class MindmapOptions
{
    public const string SectionName = "Mindmap";

    /// <summary>See <see cref="MindmapMetrics"/>: elements keep at least this fraction of a node's width between them.</summary>
    public double MinimumGapRatio { get; set; } = MindmapMetrics.Default.MinimumGapRatio;

    /// <summary>The configured metrics: the defaults, with everything this options object names applied.</summary>
    public MindmapMetrics ToMetrics() => MindmapMetrics.Default with { MinimumGapRatio = MinimumGapRatio };
}
