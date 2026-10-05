using System.Text.Json;

namespace EtAlii.Adp.Specification.Fbl.Plugins;

public sealed record PluginReadRequest(IReadOnlyList<PluginFile> Files, JsonElement? Args);
