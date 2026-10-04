namespace EtAlii.Adp.Specification.Fbl.Plugins;

public sealed record PluginTemplateRequest(string Name, IReadOnlyDictionary<string, string> Placeholders);
