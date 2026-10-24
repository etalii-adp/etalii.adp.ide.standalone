using System.Reflection;
using System.Text.Json;

namespace EtAlii.Adp.Tests.Support;

/// <summary>A module's embedded resources, read as a test checks them: the bytes the module will load.</summary>
public static class EmbeddedResources
{
    /// <summary>The bytes <paramref name="assembly"/> embeds as <paramref name="logicalName"/>; a missing one throws.</summary>
    public static byte[] ResourceBytes(this Assembly assembly, string logicalName)
    {
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} embeds no resource {logicalName}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>The JSON <paramref name="assembly"/> embeds as <paramref name="logicalName"/>.</summary>
    public static JsonElement ResourceJson(this Assembly assembly, string logicalName) =>
        JsonDocument.Parse(assembly.ResourceBytes(logicalName)).RootElement;
}
