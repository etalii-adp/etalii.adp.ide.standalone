namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>One splice of a plugin's plan, in the file it names (empty for a file body).</summary>
public sealed record PluginSplice(string File, Splice Splice);
