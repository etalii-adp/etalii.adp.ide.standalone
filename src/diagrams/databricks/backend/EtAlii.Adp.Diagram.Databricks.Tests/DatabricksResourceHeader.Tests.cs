using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// The <c>resource</c> header as the module reads it, through FBL's registration reader: what it reads, where it
/// stops, and that it reads through the shared reader. How FBL reads a header is the library's own test
/// (<c>RegistrationTests.TheResourceHeader_IsReadAsEveryHeaderIs</c>).
/// </summary>
public class DatabricksResourceHeaderTests : IDisposable
{
    private readonly TestFolder _testFolder = TestFolder.Create("databricks-resource-header");
    private readonly string _root;

    public DatabricksResourceHeaderTests()
    {
        _root = _testFolder.Path;
    }

    public void Dispose()
    {
        _testFolder.Dispose();
        GC.SuppressFinalize(this);
    }

    private string Write(string content)
    {
        var path = IoPath.Combine(_root, "plan.adp");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void TheResourceHeader_IsReadAfterTheMimeAndBodyLines()
    {
        // Arrange.
        var path = Write("databricks/pipeline\r\nbody: lakehouse.yml\r\nresource: bronze\r\n");

        // Act & assert.
        Assert.Equal("bronze", DatabricksBody.ResourceOf(path));
    }

    [Fact]
    public void AHeaderBelowTheLayoutBlock_IsNotAHeader()
    {
        // Arrange: the layout: block starts the position region; headers do not follow it.
        var path = Write("databricks/job\r\nbody: lakehouse.yml\r\nlayout:\r\n  task:ingest: 1 2\r\nresource: bronze\r\n");

        // Act & assert.
        Assert.Null(DatabricksBody.ResourceOf(path));
    }

    [Fact]
    public void AMissingHeaderAnEmptyValueAndAMissingFile_AllAnswerNull()
    {
        // Arrange.
        var path = Write("databricks/bundle\r\nbody: lakehouse.yml\r\nresource:\r\n");

        // Act & assert.
        Assert.Null(DatabricksBody.ResourceOf(path));
        Assert.Null(DatabricksBody.ResourceOf(IoPath.Combine(_root, "absent.adp")));
        Assert.Null(DatabricksBody.ResourceOf(null));
    }

    [Fact]
    public void TheRead_SucceedsWhileAWriterHoldsTheRegistration()
    {
        // Arrange: the shared-read discipline - a rename rewrites .adp headers in place, and a
        // default-share reader would refuse (and be refused by) that write on Windows. The
        // handle below is File.WriteAllText's own mode; before the shared read this scan was
        // refused here. (Guard for the 2026-09-03 family coordination ruling.)
        var path = Write("databricks/pipeline\r\nbody: lakehouse.yml\r\nresource: bronze\r\n");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act & assert.
        Assert.Equal("bronze", DatabricksBody.ResourceOf(path));
    }
}
