using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// Whether a Databricks file is text the module opens at all, judged as it always has been: by YamlDotNet, whose
/// message is the sentence of <c>databricks.unparseable</c>.
/// </summary>
/// <remarks>
/// It stands in for the readings Q4 keeps as they are in this conversion (databricks-disl-fbl findings B6 and B7):
/// a file the binding would read but YamlDotNet refuses, such as one with a key written twice, opens as unavailable as
/// it does today, and the refusal keeps YamlDotNet's own words, which no other parser produces. Nothing is read from the
/// tree it builds; the readings are the binding's (<see cref="DatabricksBody"/>).
/// </remarks>
internal static class DatabricksSyntax
{
    /// <summary>Checks <paramref name="text"/>.</summary>
    /// <exception cref="YamlException">The text is not YAML (or JSON, the same door).</exception>
    public static void Check(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        using var reader = new StringReader(text);
        new YamlStream().Load(reader);
    }
}
