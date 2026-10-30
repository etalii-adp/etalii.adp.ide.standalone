using EtAlii.Adp.Specification.Fbl;

namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// What a <c>databricks.yml</c> says: the bundle's name, its declared resources and deployment targets - the
/// structure the bundle diagram draws (Requirement 3). Its includes and variables are the bundle diagram's attributes,
/// which the binding reads for the definition and no part of the module draws.
/// </summary>
/// <param name="Name">The bundle's name; empty when the file declares none.</param>
/// <param name="Resources">Every resource declared under <c>resources:</c>, across the modelled kinds.</param>
/// <param name="Targets">The deployment targets.</param>
/// <param name="UnknownNodes">Constructs present but not modelled - drawn generically, never written.</param>
public sealed record BundleModel(
    string Name,
    IReadOnlyList<BundleResource> Resources,
    IReadOnlyList<BundleTarget> Targets,
    IReadOnlyList<UnknownNode> UnknownNodes)
{
    /// <summary>A bundle that declares nothing - the model of an empty or non-bundle file.</summary>
    public static readonly BundleModel Empty = new("", [], [], []);

    /// <summary>The bundle's reading of the file, which the bundle definition's model is built from; null for <see cref="Empty"/>.</summary>
    internal FblModel? Reading { get; init; }
}
