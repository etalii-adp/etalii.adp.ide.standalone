using EtAlii.Adp.Specification.Cel;

namespace EtAlii.Adp.Specification.Disl;

/// <summary>
/// What a derivation's expressions read as <c>env</c> (DISL §12.3): whether the diagram is read-only,
/// and the active viewpoint.
/// </summary>
/// <param name="ReadOnly">Whether nothing may be edited: <c>env.readOnly</c>, and the reason a form row gives when no other applies.</param>
/// <param name="Viewpoint">The active viewpoint's id, <c>env.viewpoint</c>; empty when the specification has none.</param>
public sealed record DislEnv(bool ReadOnly = false, string Viewpoint = "")
{
    /// <summary>The value <c>env</c> is bound to.</summary>
    public CelMap ToCel() => new() { ["readOnly"] = ReadOnly, ["viewpoint"] = Viewpoint };
}
