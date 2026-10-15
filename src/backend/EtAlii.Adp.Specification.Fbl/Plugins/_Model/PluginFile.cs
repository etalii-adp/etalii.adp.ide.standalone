namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>One file a plugin reads: the body itself (<see cref="RelativePath"/> empty) or a file of a folder subject.</summary>
public sealed record PluginFile(string RelativePath, byte[] Bytes)
{
    /// <summary>
    /// The file is larger than the host reads (<see cref="FblOptions.MaxBodyBytes"/>, FBL §16), so it is
    /// handed by its path with no bytes, and the plugin decides whether that is a finding.
    /// </summary>
    public bool IsOversized { get; init; }
}
