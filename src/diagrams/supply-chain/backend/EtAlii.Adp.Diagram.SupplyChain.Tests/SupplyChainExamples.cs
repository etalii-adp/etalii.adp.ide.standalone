namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>Where the module's own examples are, for every test that reads them.</summary>
/// <remarks>
/// <b>A missing example FAILS rather than skips</b>: the walk anchors on this module's own folder by
/// name, so it cannot climb to <c>src/examples</c> and find another corpus while reporting success.
/// </remarks>
internal static class SupplyChainExamples
{
    /// <summary>The automotive example's body.</summary>
    public static string Automotive => Find("automotive", "automotive.supply");

    /// <summary>The GPU and memory example's body.</summary>
    public static string GpuMemory => Find("gpu-memory", "gpu-memory.supply");

    private static string Find(string folder, string file) => RepositoryPaths.Under("src", "diagrams", "supply-chain", "examples", folder, file);
}
