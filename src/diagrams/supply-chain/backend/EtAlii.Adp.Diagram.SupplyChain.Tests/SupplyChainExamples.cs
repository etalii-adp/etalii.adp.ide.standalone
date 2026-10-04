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

    private static string Find(string folder, string file)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.Name == "supply-chain" && Directory.Exists(Path.Combine(directory.FullName, "backend")))
            {
                var path = Path.Combine(directory.FullName, "examples", folder, file);
                return File.Exists(path) ? path : throw new InvalidOperationException($"The {folder} example is missing: {path}");
            }
        }

        throw new InvalidOperationException(
            $"No supply-chain folder above {AppContext.BaseDirectory}, so the examples cannot be found. That is a failure, not a skip.");
    }
}
