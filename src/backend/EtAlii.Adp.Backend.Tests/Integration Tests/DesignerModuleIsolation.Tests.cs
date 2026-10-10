using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The host does not know the Knowledge designer (knowledge-designer Requirement 10.1): no core
/// source names the module's namespace or its type, so the host builds, tests and runs with the
/// module's folder deleted, and a designer module is added by adding a folder.
/// </summary>
/// <remarks>
/// <para>
/// <b>What is read.</b> Every C# source and project file of the core projects under
/// <c>src/backend</c> - the projects a module depends on, never the other way round. Test
/// projects are left out: a test of the host with the module deployed has to name what it tests.
/// </para>
/// <para>
/// <b>What it cannot see.</b> A dependency that does not spell the name: a core class finding the
/// module by reflection over a string built at run time. The discovery that exists finds modules by
/// their shape, a static <c>Designer</c> class with <c>Definitions</c>, and names none.
/// </para>
/// </remarks>
public sealed class DesignerModuleIsolationTests
{
    /// <summary>What a core file would have to write to depend on the module.</summary>
    private static readonly string[] Names = ["EtAlii.Adp.Designer.Knowledge", "etalii/knowledge"];

    private static string Backend()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = IoPath.Combine(directory.FullName, "src", "backend");
            if (Directory.Exists(candidate) && Directory.Exists(IoPath.Combine(directory.FullName, "src", "designers")))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The src/backend folder was not found above the test binary.");
    }

    private static IEnumerable<string> CoreSources() =>
        Directory.EnumerateDirectories(Backend())
            .Where(project => !IoPath.GetFileName(project).EndsWith(".Tests", StringComparison.Ordinal) && IoPath.GetFileName(project) != "EtAlii.Adp.TestSupport")
            .SelectMany(project => Directory.EnumerateFiles(project, "*", SearchOption.AllDirectories))
            .Where(file => IoPath.GetExtension(file) is ".cs" or ".csproj" or ".json" or ".props" or ".targets")
            .Where(file => !file.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal)
                           && !file.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>The files among <paramref name="files"/> that name the module, each with the name it writes.</summary>
    private static List<string> Naming(IEnumerable<(string Path, string Text)> files) =>
    [
        .. files.SelectMany(file => Names.Where(name => file.Text.Contains(name, StringComparison.Ordinal)).Select(name => $"{file.Path} names {name}")),
    ];

    [Fact]
    public void TheWalk_ReadsTheCoreProjects()
    {
        // Assert: the canary. Some hundreds of sources across the core projects; a walk that found a handful has stopped looking.
        var sources = CoreSources().ToList();
        Assert.True(sources.Count > 300, $"Only {sources.Count} core sources were found.");
        Assert.Contains(sources, file => file.EndsWith("DesignerFileRouter.cs", StringComparison.Ordinal));
        Assert.Contains(sources, file => file.EndsWith("EtAlii.Adp.Backend.Service.csproj", StringComparison.Ordinal));
    }

    [Fact]
    public void ANamedModule_IsSeen_HoweverItIsNamed()
    {
        // The guard's own controls: what a core file reaching for the module would write, and what one that does not.
        Assert.Equal(
            ["a.cs names EtAlii.Adp.Designer.Knowledge", "b.cs names etalii/knowledge"],
            Naming([("a.cs", "using EtAlii.Adp.Designer.Knowledge;"), ("b.cs", "if (origin == \"etalii/knowledge\")"), ("c.cs", "using EtAlii.Adp.Designer;")]));
    }

    [Fact]
    public void NoCoreFile_NamesTheKnowledgeDesigner()
    {
        // Act.
        var naming = Naming(CoreSources().Select(file => (IoPath.GetRelativePath(Backend(), file), File.ReadAllText(file))));

        // Assert: every one named at once.
        Assert.Empty(naming);
    }
}
