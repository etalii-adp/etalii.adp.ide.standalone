using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.HelmChart;

/// <summary>
/// One <see cref="HelmChart"/> per registered chart root, kept true as the folder changes
/// (Requirement 1.4): the module's name for the shared <see cref="IWatchedFolderStore{TSnapshot}"/>,
/// which has no save method and no method that takes anything to write.
/// </summary>
public interface IHelmChartStore : IWatchedFolderStore<HelmChart>;
