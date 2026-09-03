namespace EtAlii.Adp.Diagram.Databricks;

/// <summary>
/// What a <c>databricks.yml</c> says: the bundle's name, its includes, variables, declared
/// resources and deployment targets - the structure the bundle diagram draws (Requirement 3).
/// </summary>
/// <param name="Name">The bundle's name; empty when the file declares none.</param>
/// <param name="Includes">The <c>include:</c> globs, in file order.</param>
/// <param name="Variables">The declared variables.</param>
/// <param name="Resources">Every resource declared under <c>resources:</c>, across the modelled kinds.</param>
/// <param name="Targets">The deployment targets.</param>
/// <param name="UnknownNodes">Constructs present but not modelled - drawn generically, never written.</param>
public sealed record BundleModel(
    string Name,
    IReadOnlyList<BundleInclude> Includes,
    IReadOnlyList<BundleVariable> Variables,
    IReadOnlyList<BundleResource> Resources,
    IReadOnlyList<BundleTarget> Targets,
    IReadOnlyList<UnknownNode> UnknownNodes)
{
    /// <summary>A bundle that declares nothing - the model of an empty or non-bundle file.</summary>
    public static readonly BundleModel Empty = new("", [], [], [], [], []);
}
