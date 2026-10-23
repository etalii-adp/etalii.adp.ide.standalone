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
            .. Children(Path.Combine(TestFolderPath, "Fixtures")),
            .. Children(FindingFolders)
                .Concat(AnsibleFindingFolders.MadeFromNothing(FindingFolders).Select(name => Relative(Path.Combine(FindingFolders, name))))
                .Order(StringComparer.Ordinal),
            .. Registered(Path.Combine(ModuleFolder, "examples")),
            .. Registered(Path.Combine(SourceFolder, "examples", "diagrams", "ansible-structure")),
        ];

        static IEnumerable<string> Children(string folder) =>
            Directory.GetDirectories(folder).Select(Relative).Order(StringComparer.Ordinal);

        static IEnumerable<string> Registered(string folder) =>
            Directory.GetFiles(folder, "*.adp", SearchOption.AllDirectories)
                .Select(path => Relative(Path.GetDirectoryName(path)!))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal);

        static string Relative(string path) => Path.GetRelativePath(SourceFolder, path).Replace('\\', '/');
    }
}
