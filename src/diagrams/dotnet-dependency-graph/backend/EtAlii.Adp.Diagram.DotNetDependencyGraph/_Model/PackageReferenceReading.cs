namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>
/// One <c>PackageReference</c> as the project file declares it, with a version if one could be
/// found - written on the reference, or resolved from central package management.
/// </summary>
/// <param name="PackageId">The package's id. This alone becomes the element id, without the version.</param>
/// <param name="Version">
/// The version, or <c>null</c> for <b>not discoverable</b>. Null rather than an empty string
/// deliberately: "no version could be found" and "the version is blank" are different answers
/// and the property grid renders them differently (Requirement 4.4). A reference whose version
/// cannot be resolved still becomes an edge - dropping it would misrepresent the project.
/// </param>
public sealed record PackageReferenceReading(string PackageId, string? Version);
