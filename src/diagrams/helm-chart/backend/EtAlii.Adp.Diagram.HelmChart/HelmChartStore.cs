using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One <see cref="HelmChart"/> per registered folder, kept true as the folder changes
/// (Requirement 1.4) - the module's only reader, so two people looking at one chart cannot see
/// different charts. The watching, the claims and the settling are the shared
/// <see cref="WatchedFolderStore{TSnapshot}"/>'s; this says how a folder is read.
/// </summary>
/// <remarks>
/// A registered folder is one chart, the burst timer - a helm dependency build rewriting charts/
/// wholesale lands as one re-read (the design's burst case) - collapses a multi-file save into a
/// single re-read, and the walk is milliseconds, so every settled burst re-reads the whole chart.
/// If a chart ever appears where that is not true, the place to fix it is
/// <see cref="HelmChartReader"/>.
/// </remarks>
public sealed class HelmChartStore(HelmChartReader reader, TimeSpan? settleDelay = null)
    : WatchedFolderStore<HelmChart>(settleDelay ?? DefaultSettleDelay), IHelmChartStore
{
    /// <summary>How long a burst of changes may keep growing before one re-read answers it all.</summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly HelmChartReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>A store over the module's own reader; the shape the host and most tests want.</summary>
    public HelmChartStore()
        : this(new HelmChartReader())
    {
    }

    /// <inheritdoc />
    protected override HelmChart Read(string folder) => _reader.Read(folder);
}
