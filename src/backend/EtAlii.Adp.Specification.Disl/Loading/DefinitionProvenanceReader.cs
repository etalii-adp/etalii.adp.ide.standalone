using System.Text.Json;
using EtAlii.Adp.Specification.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>Reads a bundled definition's <c>provenance.json</c> into its <see cref="DefinitionProvenance"/>.</summary>
internal static class DefinitionProvenanceReader
{
    /// <summary>The provenance in <paramref name="json"/>, of the definition <paramref name="name"/>; each of its four fields is required.</summary>
    /// <exception cref="InvalidOperationException">A field is missing or not a string.</exception>
    public static DefinitionProvenance Read(byte[] json, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        return new DefinitionProvenance(Field("repository"), Field("path"), Field("revision"), Field("sha256"));
        string Field(string field) => JsonProperties.String(root, field) ?? throw new InvalidOperationException($"The provenance of {name} has no {field}.");
    }
}
