using EtAlii.Adp.Context;

namespace EtAlii.Adp.Hierarchy;

/// <summary>
/// The checks every hierarchy action provider needs about a target, kept here so no provider
/// has to reach into another for them.
/// </summary>
public static class HierarchyTargets
{
    /// <summary>Whether the target is still on disk - a folder for a container, a file otherwise.</summary>
    public static bool Exists(ContextTarget target) =>
        target.IsContainer ? Directory.Exists(target.ResolvedFullPath) : File.Exists(target.ResolvedFullPath);

    /// <summary>
    /// Whether the target is the project root - the folder the entries live in, which is not
    /// itself an entry and so carries no entry id. Every real entry has one; the root is the
    /// only hierarchy target built without. It exists so actions that would rename or delete
    /// the project out from under itself can tell the root apart from any other folder, which
    /// a parent-folder check cannot: only a drive root has no parent.
    /// </summary>
    public static bool IsRoot(ContextTarget target) => target.IsContainer && target.SourceId == default;
}
