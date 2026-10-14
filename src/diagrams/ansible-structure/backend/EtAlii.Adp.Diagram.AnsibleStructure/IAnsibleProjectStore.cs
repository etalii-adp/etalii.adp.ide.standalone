using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One <see cref="AnsibleProject"/> per registered folder, kept true as the tree changes: the
/// module's name for the shared <see cref="IWatchedFolderStore{TSnapshot}"/>, which has no save
/// method and no method that takes anything to write.
/// </summary>
public interface IAnsibleProjectStore : IWatchedFolderStore<AnsibleProject>;
