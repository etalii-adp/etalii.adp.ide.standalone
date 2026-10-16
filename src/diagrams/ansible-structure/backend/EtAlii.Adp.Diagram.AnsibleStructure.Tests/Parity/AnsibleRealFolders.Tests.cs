using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;
using Xunit;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// The real-file check (ansible-structure-disl-fbl, Requirement 4.7): every registered Ansible folder under
/// <c>src/</c>, read through the binding and the plugin as the validator reads it, holds per type the ids that
/// <see cref="AnsibleGraph"/> derives from <see cref="DiskAnsibleReader"/>, the reader as it was before, so
/// the plugin is never compared with itself. Every file the binding selects is one the reader reads, and
/// every position a registration stores names an element.
/// </summary>
/// <remarks>
/// The folders are found at run time, every <c>.adp</c> whose first line is the module's origin, so a folder
/// added later is checked without a change here. The minimums fail a broken enumeration rather than letting
/// it pass on nothing.
/// </remarks>
public class AnsibleRealFoldersTests
{
    /// <summary>The registered folders under <c>src/</c> when the check was written.</summary>
    private const int MinimumFolders = 13;

    /// <summary>The positions those registrations store (finding B10).</summary>
    private const int MinimumPositions = 34;

    private static readonly string[] Excluded = ["node_modules", "bin", "obj"];

    /// <summary>The names an inventory file has wherever its extension is not YAML's.</summary>
    private static readonly string[] InventoryNames = ["inventory", "inventory.yml", "inventory.yaml", "hosts", "hosts.yml", "hosts.yaml"];

    public static TheoryData<string> Folders() => [.. Registrations().Select(Relative)];

    [Fact]
    public void TheRegisteredFolders_AreFound()
    {
        Assert.True(Registrations().Count >= MinimumFolders, $"{Registrations().Count} registered folders, fewer than {MinimumFolders}");
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void TheIdsPerType_AreTheDiskReadersGraphs(string folder)
    {
        // Arrange.
        var root = Full(folder);
        var graph = AnsibleGraph.Derive(new DiskAnsibleReader().Read(root));
        var expected = graph.Nodes.Select(node => $"{Type(node.Kind)} {node.Id}")
            .Concat(graph.Edges.Select(edge => $"{edge.Kind} {edge.Id}"));

        // Act.
        var model = Read(root);
        var actual = model.Diagram.Elements.Select(element => $"{element.Type.Name} {element.Id}");

        // Assert.
        Assert.False(model.IsUnreadable, folder);
        Assert.Equal(PerType(expected), PerType(actual));
    }

    [Theory]
    [MemberData(nameof(Folders))]
    public void EverySelectedFile_IsOneTheReaderReads(string folder)
    {
        // Arrange.
        var selected = FolderSubject.Files(AnsibleDefinition.Binding, Full(folder)).Select(file => file.RelativePath).ToList();

        // Act.
        var passedOver = selected.Where(relative => !IsRead(relative, selected)).ToList();

        // Assert.
        Assert.NotEmpty(selected);
        Assert.True(passedOver.Count == 0, $"{folder}: selected and never read: {string.Join(", ", passedOver)}");
    }

    [Fact]
    public void EveryStoredPosition_NamesAnElement()
    {
        // Arrange.
        var stored = 0;
        var orphans = new List<string>();

        // Act.
        foreach (var registration in Registrations())
        {
            var ids = Read(Path.GetDirectoryName(registration)!).Diagram.Elements.Select(element => element.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var key in RegistrationLayout.Read(registration).Keys)
            {
                stored++;
                if (!ids.Contains(key)) orphans.Add($"{Relative(registration)}: {key}");
            }
        }

        // Assert.
        Assert.Empty(orphans);
        Assert.True(stored >= MinimumPositions, $"{stored} stored positions, fewer than {MinimumPositions}");
    }

    /// <summary>The registrations among them that store positions in a <c>layout:</c> block.</summary>
    private const int MinimumLayouts = 6;

    /// <summary>
    /// Every position a registration stores today is applied (ansible-structure-disl-fbl, task 10): a session
    /// opened on each registration with a <c>layout:</c> block draws every stored element at its stored
    /// position, and no registration is rewritten to make one apply.
    /// </summary>
    [Fact]
    public async Task EveryStoredPosition_ReachesAPayload_AndNoRegistrationIsRewritten()
    {
        // Arrange.
        var withLayout = Registrations().Where(registration => RegistrationLayout.Read(registration).Count > 0).ToList();
        var applied = 0;
        var misplaced = new List<string>();
        using var folders = new AnsibleFolders();

        // Act.
        foreach (var registration in withLayout)
        {
            var before = await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken);
            var stored = RegistrationLayout.Read(registration);
            await using (var session = new AnsibleSession(Path.GetDirectoryName(registration)!, folders, new AnsibleElementMapper(), registration))
            {
                var drawn = session.Baseline().OfType<DiagramAddDelta>().SelectMany(delta => delta.Elements)
                    .ToDictionary(element => element.Id, StringComparer.Ordinal);
                foreach ((string id, RegistrationPosition position) in stored)
                {
                    if (drawn.TryGetValue(id, out var element) && new RegistrationPosition(element.X, element.Y).Equals(position)) applied++;
                    else misplaced.Add($"{Relative(registration)}: {id}");
                }
            }

            Assert.Equal(before, await File.ReadAllBytesAsync(registration, TestContext.Current.CancellationToken));
        }

        // Assert.
        Assert.Empty(misplaced);
        Assert.True(withLayout.Count >= MinimumLayouts, $"{withLayout.Count} registrations with a layout block, fewer than {MinimumLayouts}");
        Assert.True(applied >= MinimumPositions, $"{applied} applied positions, fewer than {MinimumPositions}");
    }

    /// <summary>The folder read as the validator reads it: once, through the binding and the plugin.</summary>
    private static DislModel Read(string root) =>
        AnsibleDefinition.Model(FolderBody.ReadOnce(root, AnsibleDefinition.Binding, new AnsibleFolderPlugin(new AnsibleFolderListing(root))));

    /// <summary>
    /// Whether the reader reads a selected file, by Ansible's layout and nothing else: a YAML file at the root,
    /// under <c>playbooks/</c> and under a role's <c>tasks/</c>; an <c>inventory</c> or <c>hosts</c> file at the
    /// root; a role's <c>meta/main.yml</c>, and its <c>meta/main.yaml</c> only where it has no <c>meta/main.yml</c>;
    /// and in an environment, a file with a YAML extension or none, or an inventory's name in any case.
    /// </summary>
    private static bool IsRead(string relative, IReadOnlyList<string> selected) =>
        relative.Split('/') switch
        {
            [var name] => IsYaml(name) || name is "inventory" or "hosts",
            ["playbooks", var name] => IsYaml(name),
            ["roles", _, "tasks", var name] => IsYaml(name),
            ["roles", _, "meta", "main.yml"] => true,
            ["roles", var role, "meta", "main.yaml"] => !selected.Contains($"roles/{role}/meta/main.yml", StringComparer.Ordinal),
            ["inventories", _, var name] => IsYaml(name) || Path.GetExtension(name).Length == 0 || InventoryNames.Contains(name, StringComparer.OrdinalIgnoreCase),
            _ => false,
        };

    private static bool IsYaml(string name) => Path.GetExtension(name) is ".yml" or ".yaml";

    private static List<string> PerType(IEnumerable<string> lines) => [.. lines.Order(StringComparer.Ordinal)];

    private static string Type(AnsibleNodeKind kind) => kind.ToString();

    /// <summary>Every <c>.adp</c> under <c>src/</c> that registers this module's origin, in ordinal order.</summary>
    private static List<string> Registrations()
    {
        var found = new List<string>();
        Walk(RepositoryPaths.Under("src"));
        return [.. found.Where(path => File.ReadLines(path).FirstOrDefault()?.Trim() == Diagram.AnsibleStructure.Origin.Key).Order(StringComparer.Ordinal)];

        void Walk(string folder)
        {
            found.AddRange(Directory.EnumerateFiles(folder, "*.adp"));
            foreach (var child in new DirectoryInfo(folder).EnumerateDirectories())
            {
                if ((child.Attributes & FileAttributes.ReparsePoint) != 0 || Excluded.Contains(child.Name, StringComparer.Ordinal)) continue;
                Walk(child.FullName);
            }
        }
    }

    private static string Relative(string registration) =>
        Path.GetRelativePath(RepositoryPaths.Under("src"), Path.GetDirectoryName(registration)!).Replace('\\', '/');

    private static string Full(string folder) => Path.Combine(RepositoryPaths.Under("src"), folder);
}
