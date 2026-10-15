namespace EtAlii.Adp.Specification.Fbl.Plugins;

public sealed record PluginReadRequest(IReadOnlyList<PluginFile> Files)
{
    /// <summary>
    /// The entries a host adds beside the selected files (<see cref="FblOptions.SupplementaryEntries"/>),
    /// each a path relative to the folder, with no bytes, in ordinal order. Outside FBL 0.2; empty for a file body.
    /// </summary>
    public IReadOnlyList<string> SupplementaryEntries { get; init; } = [];
}
