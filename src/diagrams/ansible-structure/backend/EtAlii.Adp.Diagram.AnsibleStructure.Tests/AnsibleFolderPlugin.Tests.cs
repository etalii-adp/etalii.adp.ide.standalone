using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Plugins;
using EtAlii.Adp.Specification.Fbl.Routing;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The FBL persistence plugin of the Ansible folder (ansible-structure-disl-fbl, Requirement 4.2): it reads
/// what it is handed, gives the same elements for the same bytes, opens no file, and is never asked to plan.
/// </summary>
public sealed class AnsibleFolderPluginTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.AnsibleFolderPlugin");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void TheSameBytes_GiveTheSameElements()
    {
        // Arrange: one fixture's selected files, read once, handed twice to two plugins.
        var root = IoPath.GetFullPath(IoPath.Combine("Fixtures", "infrastructure"));
        var files = Selected(root);

        // Act.
        var first = new AnsibleFolderPlugin(new AnsibleFolderListing(root)).Read(new PluginReadRequest(files));
        var second = new AnsibleFolderPlugin(new AnsibleFolderListing(root)).Read(new PluginReadRequest([.. files.Select(file => file with { Bytes = [.. file.Bytes] })]));

        // Assert.
        Assert.NotEmpty(first.Elements);
        Assert.Equal(Lines(first), Lines(second));
    }

    [Fact]
    public void AHandedFileSetWithNoDiskBehindIt_Reads()
    {
        // Arrange: a folder that is not there, and two files that exist only as bytes.
        var nowhere = _folder.Combine("not-there");
        var listing = new AnsibleFolderListing(nowhere);
        PluginFile[] files =
        [
            new("site.yml", "- name: Web\r\n  hosts: web\r\n  roles:\r\n    - nginx\r\n"u8.ToArray()),
            new("hosts", "[web]\r\nweb-01\r\n"u8.ToArray()),
        ];

        // Act.
        var result = new AnsibleFolderPlugin(listing).Read(new PluginReadRequest(files));

        // Assert.
        Assert.False(Directory.Exists(nowhere));
        Assert.Equal(
            [
                "Playbook playbook:site.yml site.yml:0",
                "Inventory inventory:hosts hosts:0",
                "UsesRole edge:playbook:site.yml|UsesRole|nginx site.yml:4 playbook:site.yml -> (none)",
                "Targets edge:playbook:site.yml|Targets|web|inventory:hosts :1 playbook:site.yml -> inventory:hosts",
            ],
            result.Elements.Select(Line));
        Assert.Empty(result.Findings);
    }

    /// <summary>A file that does not parse is one finding at its file, with the words and the line the reader of a file on disk gives.</summary>
    [Fact]
    public void AFileThatDoesNotParse_IsOneFindingWithTheWordsAndLineOfAFileOnDisk()
    {
        // Arrange: the same bytes, handed and on disk.
        var bytes = "- name: Web\r\n  hosts: all\r\n  roles: [web\r\n"u8.ToArray();
        var onDisk = _folder.Combine("site.yml");
        File.WriteAllBytes(onDisk, bytes);
        var expected = Assert.IsType<TolerantYamlUnreadable>(TolerantYaml.Read(onDisk, "site.yml")).Failure;

        // Act.
        var result = new AnsibleFolderPlugin(new AnsibleFolderListing(_folder.Combine("not-there"))).Read(new PluginReadRequest([new PluginFile("site.yml", bytes)]));

        // Assert.
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingCodes.Unparseable, finding.Code);
        Assert.Equal(("site.yml", (int)expected.Line, expected.Message), (finding.Location?.File, finding.Location?.Line, finding.Message));
        Assert.True(expected.Line > 0, $"line {expected.Line}");
        Assert.Empty(result.Elements);
    }

    [Fact]
    public void AChangeIsRefusedWithTheBindingsReason_AndThePluginIsNeverAskedToPlan()
    {
        // Arrange.
        var root = IoPath.GetFullPath(IoPath.Combine("Fixtures", "infrastructure"));
        using var body = FolderBody.Open(root, AnsibleDefinition.Binding, new AnsibleFolderPlugin(new AnsibleFolderListing(root)));

        // Act: the plugin throws if it is asked.
        var result = body.Plan(new ModelChange.Set("role:nginx", new Dictionary<string, object?> { ["name"] = "renamed" }));

        // Assert.
        Assert.True(body.IsReadOnly);
        Assert.Equal(AnsibleDefinition.WriteRefusal, Assert.IsType<PlanResult.Refused>(result).Reason);
    }

    [Fact]
    public void TheFoldersItCountsIn_AreWhatTheFolderBodyWatches()
    {
        // Arrange.
        var root = IoPath.GetFullPath(IoPath.Combine("Fixtures", "infrastructure"));

        // Act.
        using var body = FolderBody.Open(root, AnsibleDefinition.Binding, new AnsibleFolderPlugin(new AnsibleFolderListing(root)));

        // Assert: a counted folder no file rule reaches, and the annotation no file rule selects.
        Assert.Contains(IoPath.Combine(root, "roles"), body.Dependencies);
        Assert.Contains(IoPath.Combine(root, "ansible.cfg"), body.Dependencies);
    }

    private static List<PluginFile> Selected(string root) =>
        [.. FolderSubject.Files(AnsibleDefinition.Binding, root).Select(file => new PluginFile(file.RelativePath, File.ReadAllBytes(file.FullPath)))];

    private static List<string> Lines(PluginReadResult result) =>
        [.. result.Elements.Select(element => Line(element) + " " + string.Join(", ", element.Attributes.Select(pair => $"{pair.Key}={Value(pair.Value)}"))),
         .. result.Findings.Select(finding => finding.ToString())];

    private static string Line(FblElement element) =>
        $"{element.Type} {element.Id} {element.File}:{element.Line}" + (element.IsRelation ? $" {element.Source} -> {element.Target ?? "(none)"}" : "");

    private static string Value(object? value) => value switch
    {
        null => "null",
        string text => text,
        IEnumerable<KeyValuePair<string, object?>> map => "{" + string.Join(", ", map.Select(pair => $"{pair.Key}={Value(pair.Value)}")) + "}",
        System.Collections.IEnumerable list => "[" + string.Join(", ", list.Cast<object?>().Select(Value)) + "]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };
}
