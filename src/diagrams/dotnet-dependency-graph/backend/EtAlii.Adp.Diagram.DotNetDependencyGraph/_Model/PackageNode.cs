namespace EtAlii.Adp.Diagram.DotNetDependencyGraph;

/// <summary>One NuGet package the solution consumes, however many projects consume it.</summary>
/// <param name="Id">
/// <c>package:&lt;package id&gt;</c> - <b>deliberately without the version</b>. Keying on
/// id-plus-version would make differing versions free, but a version bump would then change the
/// id and silently lose the user's stored position on the single commonest change a solution
/// undergoes, breaching Requirement 7.2's "a refresh must not cost the user their arrangement".
/// </param>
/// <param name="Versions">
/// Every version the solution's projects ask for, ordered. More than one is not an error: it is
/// the thing a reader most wants surfaced, which is why it is carried here and marked below
/// rather than split into two disconnected boxes (Requirement 3.5).
/// </param>
/// <param name="HasVersionConflict">
/// Whether the projects disagree about the version. <b>Collapsing loudly</b> - which is what
/// Requirement 3.5 asks for, as against the silent collapse it forbids.
/// </param>
/// <param name="Description">
/// From the local NuGet cache, or <c>null</c> for not obtainable - never an error and never a
/// wait (Requirement 5.3). Filled in by the description reader, not by the graph.
/// </param>
public sealed record PackageNode(
    string Id,
    string PackageId,
    IReadOnlyList<string> Versions,
    bool HasVersionConflict,
    string? Description = null);
