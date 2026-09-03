using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The registration header facility, shared-machinery item 10: a reading's own <c>key: value</c>
/// line after core's headers and before the <c>layout:</c> block, scanned here once for the
/// whole family - it scans, the reading interprets.
/// </summary>
public class RdfRegistrationHeadersTests : IDisposable
{
    private readonly string _root;

    public RdfRegistrationHeadersTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string content)
    {
        var path = IoPath.Combine(_root, "graph.adp");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AHeaderAfterBodyAndView_IsRead()
    {
        // Arrange.
        var path = Write("w3c/skos\r\nbody: vocab.ttl\r\nview: something\r\nlanguage: nl\r\nlayout:\r\n  res:x: 1 2\r\n");

        // Act & assert.
        Assert.Equal("nl", RdfRegistrationHeaders.Read(path, "language"));
    }

    [Fact]
    public void AHeaderBelowTheLayoutBlock_IsNotAHeader()
    {
        // Arrange: the layout: block starts the position region; headers do not follow it.
        var path = Write("w3c/rdf\r\nbody: graph.ttl\r\nlayout:\r\n  res:x: 1 2\r\nlanguage: nl\r\n");

        // Act & assert.
        Assert.Null(RdfRegistrationHeaders.Read(path, "language"));
    }

    [Fact]
    public void AMissingHeaderAnEmptyValueAndAMissingFile_AllAnswerNull()
    {
        // Arrange.
        var path = Write("w3c/rdf\r\nbody: graph.ttl\r\nlanguage:\r\n");

        // Act & assert.
        Assert.Null(RdfRegistrationHeaders.Read(path, "language"));
        Assert.Null(RdfRegistrationHeaders.Read(path, "profile"));
        Assert.Null(RdfRegistrationHeaders.Read(IoPath.Combine(_root, "absent.adp"), "language"));
        Assert.Null(RdfRegistrationHeaders.Read(null, "language"));
    }
}
