using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// More than one editor claims <paramref name="Extension"/> and none is the declared default -
/// a deployment conflict, reported at startup and degraded per file rather than crashing the
/// host (modular-text-editors Requirement 4.3, the corrected precedent: two editors clashing
/// over an extension must not take diagram functionality down with them).
/// </summary>
public sealed record EditorAmbiguous(string Extension, IReadOnlyList<EditorDefinition> Claimants) : EditorRouting;
