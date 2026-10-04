namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>One file a plugin reads: the body itself (<see cref="RelativePath"/> empty) or a file of a folder subject.</summary>
public sealed record PluginFile(string RelativePath, byte[] Bytes);
