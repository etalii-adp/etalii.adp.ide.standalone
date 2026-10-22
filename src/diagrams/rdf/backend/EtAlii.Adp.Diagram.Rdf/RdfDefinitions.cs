using System.Text;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Routing;

namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>
/// The four readings of a Turtle or N-Triples body, each with its DISL definition bundled from etalii-adp/etalii.adp
/// into <c>definition/</c> (<c>w3c-rdf.dis</c> and the three beside it), loaded once as one
/// <see cref="BundledDefinitionSet"/>, with the wire ids of its own <c>x-</c> block; and the module's own FBL binding,
/// <c>w3c-turtle.fbl#turtle</c>, which the four name as their persistence and whose templates are the new documents.
/// </summary>
/// <remarks>
/// Nothing derives from the definitions yet (rdf-disl-fbl task 6). Their plugin functions are the turtle plugin's,
/// which this module does not implement yet, so they load without implementations.
/// </remarks>
internal static class RdfDefinitions
{
    /// <summary>Each reading, with the resource its definition is embedded as and the key of its block of wire ids.</summary>
    internal static IReadOnlyList<(ToolOrigin Origin, string Resource, string WireIds)> Readings { get; } =
    [
        (ServiceCollectionAddRdfExtension.RdfOrigin, "w3c-rdf.dis", "x-rdf"),
        (ServiceCollectionAddRdfExtension.OwlOrigin, "w3c-owl.dis", "x-owl"),
        (ServiceCollectionAddSkosExtension.SkosOrigin, "w3c-skos.dis", "x-skos"),
        (Shacl.ServiceCollectionAddShaclExtension.ShaclOrigin, "w3c-shacl.dis", "x-shacl"),
    ];

    /// <summary>Loaded on first use, so registering the readings loads nothing.</summary>
    private static readonly Lazy<BundledDefinitionSet> _set = new(() => BundledDefinitionSet.Load(
        typeof(RdfDefinitions).Assembly,
        DislPluginFunctions.None,
        Readings.Select(reading => reading.Resource)));

    private static readonly Lazy<FblBinding> _binding = new(() => BundledBinding.Load(typeof(RdfDefinitions).Assembly, "w3c-turtle.fbl", "turtle"));

    /// <summary>The four definitions, in the order of <see cref="Readings"/>.</summary>
    /// <exception cref="InvalidOperationException">A definition is missing, is not the bytes its provenance names, or does not load.</exception>
    public static BundledDefinitionSet Set => _set.Value;

    /// <summary>The binding every body of the four readings is read with.</summary>
    public static FblBinding Binding => _binding.Value;

    /// <summary>The definition of the reading <paramref name="origin"/> names.</summary>
    /// <exception cref="KeyNotFoundException">No reading has that origin.</exception>
    public static BundledDefinition For(ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        return Set.For(origin.Key);
    }

    /// <summary>The wire ids of the reading <paramref name="origin"/> names, from its own <c>x-</c> block.</summary>
    /// <exception cref="KeyNotFoundException">No reading has that origin.</exception>
    public static WireIdMap Ids(ToolOrigin origin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        var reading = Readings.FirstOrDefault(candidate => candidate.Origin == origin);
        return reading.Resource is null
            ? throw new KeyNotFoundException($"'{origin.Key}' is not a reading of a Turtle or N-Triples body.")
            : WireIdMap.Of(For(origin).Specification, reading.WireIds);
    }

    /// <summary>
    /// The new document of the reading <paramref name="origin"/> names, for a file whose base name is
    /// <paramref name="baseName"/>: the binding's template for that origin (FBL §13).
    /// </summary>
    public static string NewDocument(ToolOrigin origin, string baseName)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(baseName);
        var bytes = TemplateWriter.Produce(Binding, origin.Key, baseName + ".ttl", rule => throw new InvalidOperationException($"The template asks for a new id of '{rule}', which no template of this binding does."))
            ?? throw new InvalidOperationException("The binding has no template.");
        return Encoding.UTF8.GetString(bytes);
    }
}
