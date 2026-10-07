using EtAlii.Adp.Context;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Documents.Wire;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The editor family's one property provider (modular-text-editors Requirements 9.1-9.3):
/// four facts about the file, every one read-only with a reason, contributed once for the
/// whole family and never for a diagram-routed file.
/// </summary>
public class EditorFilePropertyProviderTests : IDisposable
{
    private readonly string _root;
    private readonly EditorFilePropertyProvider _provider;

    public EditorFilePropertyProviderTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _provider = new EditorFilePropertyProvider(new DiagramFileRouter(new TestDiagramDefinitionCatalog(Mindmap)));
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private static readonly DiagramDefinition Mindmap =
        new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private ContextTarget TargetOf(string fileName, byte[] bytes)
    {
        var fullPath = IoPath.Combine(_root, fileName);
        File.WriteAllBytes(fullPath, bytes);
        return new ContextTarget(ContextScope.Hierarchy, fullPath, IsContainer: false, ShortGuid.NewShortGuid(), RootPath: _root);
    }

    [Fact]
    public async Task ATextFile_ShowsItsFourFacts_AllReadOnlyWithAReason()
    {
        // Arrange: UTF-8 with BOM, CRLF - the detected values are the shown values.
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "one\r\ntwo\r\n"u8];

        // Act.
        var properties = await _provider.DescribeAsync(TargetOf("notes.txt", bytes), TestContext.Current.CancellationToken);

        // Assert (Requirement 9.2: facts about the file, each saying why it cannot be edited here).
        Assert.Equal(["editor.encoding", "editor.line-endings", "editor.size", "editor.line-count"], properties.Select(property => property.Id));
        Assert.All(properties, property => Assert.NotEqual("", property.ReadOnlyReason));
        Assert.Equal("UTF-8 with BOM", properties[0].Value);
        Assert.Equal("CRLF", properties[1].Value);
        Assert.Equal($"{bytes.Length} bytes", properties[2].Value);
        Assert.Equal("2", properties[3].Value);
    }

    [Fact]
    public async Task ADiagramRoutedFile_GetsNothingFromThisProvider()
    {
        // Arrange and act: a .mm belongs to its diagram module's grid, not the editor family's.
        var properties = await _provider.DescribeAsync(TargetOf("map.mm", "root\n"u8.ToArray()), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(properties);
    }

    [Fact]
    public async Task ABinaryFile_ContributesNothing()
    {
        // Arrange and act: the refusal reaches the user when they open the file; a grid of
        // half-answers would not help them here.
        var properties = await _provider.DescribeAsync(
            TargetOf("blob.bin", [0x00, 0x01, 0x02, 0xFF]), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Empty(properties);
    }

    [Fact]
    public async Task Setting_IsRefusedWithTheSameReason()
    {
        // Arrange and act (Requirement 9.2: the grid shows the reason; a write attempt hears it).
        var result = await _provider.SetAsync(
            TargetOf("notes.txt", "x\n"u8.ToArray()), "editor.line-endings", "LF", TestContext.Current.CancellationToken);

        // Assert.
        Assert.NotEqual("", result.Error);
    }
}
