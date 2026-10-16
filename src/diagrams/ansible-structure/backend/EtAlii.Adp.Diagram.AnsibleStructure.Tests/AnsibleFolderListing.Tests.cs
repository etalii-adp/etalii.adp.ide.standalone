using System.Text;
using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The seam of Requirement 4.3 (ansible-structure-disl-fbl, gaps 1 and 2, finding B11): what the plugin
/// learns from the folder that FBL cannot hand it, against the cases each gap names.
/// </summary>
public sealed class AnsibleFolderListingTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.AnsibleFolderListing");

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void AnEmptyFolder_IsThere_AndHoldsNoFile()
    {
        // Arrange: gap 1, a role that is a folder and nothing else.
        Directory.CreateDirectory(_folder.Combine("roles", "empty"));
        var listing = new AnsibleFolderListing(_folder.Path);

        // Act and assert.
        Assert.True(listing.FolderExists("roles/empty"));
        Assert.False(listing.FolderExists("roles/absent"));
        Assert.Equal(["empty"], listing.Folders("roles"));
        Assert.Equal(0, listing.FileCount("roles/empty"));
    }

    [Fact]
    public void AFileThatCannotBeRead_IsCounted_SoCountingReadsNothing()
    {
        // Arrange: gap 2. A link to nothing is a file a listing reports and a read refuses.
        Write("roles/web/templates/site.conf.j2", "server {}");
        File.CreateSymbolicLink(_folder.Combine("roles", "web", "templates", "gone.j2"), "nothing-here.j2");
        var listing = new AnsibleFolderListing(_folder.Path);

        // Act.
        var count = listing.FileCount("roles/web/templates");

        // Assert.
        Assert.Equal(2, count);
    }

    [Fact]
    public void ALinkedFolderFoundByListing_IsSkipped_AndOneNamedByTheLayoutIsEntered()
    {
        // Arrange: a role that is a link, and a playbooks folder that is one.
        Write("elsewhere/role/tasks/main.yml", "- debug: msg=hi\r\n");
        Write("elsewhere/plays/deploy.yml", "- hosts: all\r\n");
        Write("roles/real/tasks/main.yml", "- debug: msg=hi\r\n");
        Directory.CreateSymbolicLink(_folder.Combine("roles", "linked"), IoPath.Combine("..", "elsewhere", "role"));
        Directory.CreateSymbolicLink(_folder.Combine("playbooks"), IoPath.Combine("elsewhere", "plays"));
        var listing = new AnsibleFolderListing(_folder.Path);

        // Act.
        var linked = listing.LinkedFiles(AnsibleDefinition.Binding);

        // Assert.
        Assert.Equal(["real"], listing.Folders("roles"));
        Assert.Equal(["playbooks/deploy.yml"], linked.Select(file => file.RelativePath));
        Assert.Equal("- hosts: all\r\n", Encoding.UTF8.GetString(linked[0].Bytes));
    }

    [Fact]
    public void ALinkedFile_IsReadWithItsBytes_AndAFileThatIsNoLinkIsLeftToTheFolderSubject()
    {
        // Arrange: finding B11, a playbook that is a link to a file.
        Write("elsewhere/play.yml", "- hosts: web\r\n");
        Write("site.yml", "- hosts: all\r\n");
        File.CreateSymbolicLink(_folder.Combine("linked.yml"), IoPath.Combine("elsewhere", "play.yml"));
        var listing = new AnsibleFolderListing(_folder.Path);

        // Act.
        var linked = listing.LinkedFiles(AnsibleDefinition.Binding);

        // Assert.
        var file = Assert.Single(linked);
        Assert.Equal("linked.yml", file.RelativePath);
        Assert.Equal("- hosts: web\r\n", Encoding.UTF8.GetString(file.Bytes));
    }

    [Fact]
    public void AFolderThatIsNotThere_ListsNothing()
    {
        // Arrange.
        var listing = new AnsibleFolderListing(_folder.Combine("not-there"));

        // Act and assert.
        Assert.False(listing.FolderExists(""));
        Assert.Empty(listing.Folders("roles"));
        Assert.Equal(0, listing.FileCount(""));
        Assert.Empty(listing.LinkedFiles(AnsibleDefinition.Binding));
    }

    private void Write(string relative, string text)
    {
        var path = _folder.Combine(relative.Split('/'));
        Directory.CreateDirectory(IoPath.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }
}
