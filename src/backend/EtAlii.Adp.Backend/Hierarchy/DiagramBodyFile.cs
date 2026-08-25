using EtAlii.Adp.Diagram;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The body document a registration opens, and whether the registration owns it.
/// <para>
/// Ownership is the whole point of the distinction. A derived sibling belongs to its one
/// <c>.adp</c>, so deleting or renaming the registration takes the body with it. A body
/// named by a <c>body:</c> header may be shared by several registrations - which is how
/// C4's "model once, view many" works - so deleting one view must leave the model alone
/// (c4-diagrams Requirement 2.4).
/// </para>
/// </summary>
/// <param name="Path">The body document's full path, whether or not it exists on disk.</param>
/// <param name="ViewKey">The view within it this registration opens, or null when it names none.</param>
/// <param name="IsOwned">Whether the registration owns the body and may take it along on delete and rename.</param>
public readonly record struct DiagramBodyFile(string Path, string? ViewKey, bool IsOwned);
