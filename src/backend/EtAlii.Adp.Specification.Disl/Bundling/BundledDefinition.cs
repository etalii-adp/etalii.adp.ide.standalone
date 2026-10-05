using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// Where a bundled definition came from (<c>provenance.json</c>, written by
/// <c>src/diagrams/tools/bundle-disl.sh</c>): the repository, the file in it, the full revision it was
/// copied at, and the sha256 of the bundled bytes.
/// </summary>
public sealed record DefinitionProvenance(string Repository, string Path, string Revision, string Sha256);

/// <summary>A specification bundled into a module's assembly, loaded, with its provenance.</summary>
public sealed record BundledDefinition(DislSpecification Specification, DefinitionProvenance Provenance, IReadOnlyList<DislDiagnostic> Diagnostics)
{
    /// <summary>
    /// Loads the definition <paramref name="assembly"/> embeds as <paramref name="logicalName"/>
    /// (<c>gartner-hype-cycle-graph.dis</c>), and its provenance, embedded beside it as
    /// <c>&lt;name&gt;.provenance.json</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A resource is missing, the bytes are not the ones the provenance names, or the specification
    /// does not load: a module cannot run from a definition it does not have.
    /// </exception>
    public static BundledDefinition Load(Assembly assembly, string logicalName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(logicalName);

        var bytes = Resource(assembly, logicalName);
        var name = logicalName.EndsWith(".dis", StringComparison.Ordinal) ? logicalName[..^".dis".Length] : logicalName;
        var provenance = ProvenanceOf(Resource(assembly, name + ".provenance.json"), name);

        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (!string.Equals(actual, provenance.Sha256, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{logicalName} hashes to {actual}, not to the {provenance.Sha256} its provenance records; re-bundle it with src/diagrams/tools/bundle-disl.sh.");
        }

        var loaded = DislLoader.Load(bytes);
        if (loaded.Specification is null)
        {
            throw new InvalidOperationException($"{logicalName} does not load: {string.Join("; ", loaded.Errors)}");
        }
        return new BundledDefinition(loaded.Specification, provenance, loaded.Diagnostics);
    }

    private static byte[] Resource(Assembly assembly, string logicalName)
    {
        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} embeds no resource {logicalName}.");
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private static DefinitionProvenance ProvenanceOf(byte[] json, string name)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        string Field(string field) => DislJson.String(root, field) ?? throw new InvalidOperationException($"The provenance of {name} has no {field}.");
        return new DefinitionProvenance(Field("repository"), Field("path"), Field("revision"), Field("sha256"));
    }
}
