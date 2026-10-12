using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Editor.Json.Tests;

/// <summary>
/// What the module's folder must not hold (json-editor Requirement 10.3, ruling Q6): no EDSL
/// specification is invented for this editor before the language has content.
/// </summary>
public class ModuleFolderTests
{
    private static string ModuleRoot { get; } = RepositoryPaths.Under("src", "editors", "json");

    [Fact]
    public void TheModule_HoldsNoEdsFile()
    {
        // Arrange: the walk's liveness - an empty or wrong root would pass over nothing.
        var files = Directory.EnumerateFiles(ModuleRoot, "*", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file))
            .ToList();
        Assert.Contains(files, file => IoPath.GetFileName(file) == "Editor.cs");

        // Act.
        var eds = files.Where(file => IoPath.GetExtension(file).Equals(".eds", StringComparison.OrdinalIgnoreCase)).ToList();

        // Assert.
        Assert.Empty(eds);
    }

    private static bool IsBuildOutput(string file)
    {
        var segments = file.Split(IoPath.DirectorySeparatorChar, IoPath.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj") || segments.Contains("node_modules");
    }
}
