using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Fbl.Plugins;

public sealed record PluginPlanRequest(IReadOnlyList<PluginFile> Files, PluginReadResult Last, ModelChange Change);
