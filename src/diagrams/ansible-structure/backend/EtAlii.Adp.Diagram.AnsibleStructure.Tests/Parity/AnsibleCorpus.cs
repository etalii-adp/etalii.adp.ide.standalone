using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The Ansible structure diagram's parity corpus, each part in ordinal order: the fixture trees under the
/// test project's <c>Fixtures/</c>; one folder per finding marked <i>read</i>, under <c>Parity/Folders/</c> or
/// made by <see cref="AnsibleFindingFolders"/> where git cannot hold it; and every registered folder of the
/// module's <c>examples/</c> and of <c>src/examples/diagrams/ansible-structure/</c>.
/// </summary>
internal static class AnsibleCorpus
{
    /// <summary>This test project's name.</summary>
    public const string TestProject = "EtAlii.Adp.Diagram.AnsibleStructure.Tests";

    /// <summary>The module's folder, <c>src/diagrams/ansible-structure</c>.</summary>
    private static string ModuleFolder { get; } = RepositoryPaths.Under("src", "diagrams", "ansible-structure");

    /// <summary>The test project's folder.</summary>
    public static string TestFolderPath => Path.Combine(ModuleFolder, "backend", TestProject);

    /// <summary>The finding folders git can hold.</summary>
    private static string FindingFolders => Path.Combine(TestFolderPath, "Parity", "Folders");

    /// <summary>The repository's <c>src</c> folder, which every corpus path is relative to.</summary>
    public static string SourceFolder => Path.GetFullPath(Path.Combine(ModuleFolder, "..", ".."));

    /// <summary>The corpus, as folder paths relative to <c>src/</c> with forward slashes, in the order they are recorded.</summary>
    public static IReadOnlyList<string> Folders()
    {
        return
        [
            .. RepositoryPaths.SortedRelativeToSource(Directory.GetDirectories(Path.Combine(TestFolderPath, "Fixtures"))),
            .. RepositoryPaths.SortedRelativeToSource(Directory.GetDirectories(FindingFolders)
                .Concat(AnsibleFindingFolders.MadeFromNothing(FindingFolders).Select(name => Path.Combine(FindingFolders, name)))),
            .. Registered(Path.Combine(ModuleFolder, "examples")),
            .. Registered(Path.Combine(SourceFolder, "examples", "diagrams", "ansible-structure")),
        ];
    }

    /// <summary>Every folder under <paramref name="folder"/> that holds a registration, once, as the corpus lists them.</summary>
    private static IEnumerable<string> Registered(string folder) =>
        RepositoryPaths.SortedRelativeToSource(Directory.GetFiles(folder, "*.adp", SearchOption.AllDirectories)
            .Select(path => Path.GetDirectoryName(path)!)
            .Distinct(StringComparer.Ordinal));
}
