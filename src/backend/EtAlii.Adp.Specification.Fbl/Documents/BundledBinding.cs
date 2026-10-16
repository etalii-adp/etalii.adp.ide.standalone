using System.Reflection;

namespace EtAlii.Adp.Specification.Fbl.Documents;

/// <summary>
/// An FBL document a module embeds in its assembly, loaded: how every module reads the binding it is
/// bundled with, as <c>BundledDefinition</c> is how it reads its DISL definition.
/// </summary>
public static class BundledBinding
{
    /// <summary>Loads the document <paramref name="assembly"/> embeds as <paramref name="logicalName"/> (<c>timeline.fbl</c>).</summary>
    /// <exception cref="InvalidOperationException">
    /// The resource is missing or the document does not load: a module cannot run from a binding it
    /// does not have.
    /// </exception>
    public static FblDocument Load(Assembly assembly, string logicalName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrEmpty(logicalName);

        using var stream = assembly.GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} embeds no resource {logicalName}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var problems = FblDocumentLoader.Load(buffer.ToArray(), out var document);
        return document ?? throw new InvalidOperationException($"{logicalName} does not load: {string.Join("; ", problems)}");
    }

    /// <summary>The binding <paramref name="name"/> of the document <paramref name="assembly"/> embeds as <paramref name="logicalName"/>.</summary>
    /// <exception cref="InvalidOperationException">The document does not load, or holds no binding of that name.</exception>
    public static FblBinding Load(Assembly assembly, string logicalName, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return Load(assembly, logicalName).Bindings.TryGetValue(name, out var binding)
            ? binding
            : throw new InvalidOperationException($"{logicalName} has no binding {name}.");
    }
}
