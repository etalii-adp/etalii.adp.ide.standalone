using EtAlii.Adp.Backend.Context;

namespace EtAlii.Adp.Backend.Hierarchy;

/// <summary>
/// The checks every hierarchy action provider needs about a target, kept here so no provider
/// has to reach into another for them.
/// </summary>
public static class HierarchyTargets
{
    /// <summary>Whether the target is still on disk - a folder for a container, a file otherwise.</summary>
    public static bool Exists(ContextTarget target) =>
        target.IsContainer ? Directory.Exists(target.ResolvedFullPath) : File.Exists(target.ResolvedFullPath);
}
