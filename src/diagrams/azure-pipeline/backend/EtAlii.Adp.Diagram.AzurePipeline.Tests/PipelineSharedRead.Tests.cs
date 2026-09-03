using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// This module's two file reads against the world's write side: a pipeline load and a template
/// resolve must both succeed while a writer holds the file, per the sharing discipline
/// SharedDocumentReader carries. Windows enforces sharing, so these guards bite there.
/// </summary>
public class PipelineSharedReadTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-shared-" + Guid.NewGuid().ToString("N"));

    public PipelineSharedReadTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private const string Simple = "stages:\n  - stage: Build\n    jobs:\n      - job: Compile\n        steps:\n          - script: x\n";

    private string Write(string name, string content)
    {
        var full = IoPath.Combine(_workspace, name);
        File.WriteAllText(full, content);
        return full;
    }

    [Fact]
    public void GetOrLoad_ReadsADocumentAnEditorIsStillWriting()
    {
        // Arrange: the handle every save holds - write access, sharing only reads, the mode
        // File.WriteAllText opens with.
        var full = Write("azure-pipelines.yml", Simple);
        using var editor = new FileStream(full, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var entry = new PipelineDocumentStore().GetOrLoad(_workspace, full);

        // Assert: the document itself, not the empty fallback.
        Assert.True(entry.IsUsable);
        Assert.Equal(Simple, entry.Document.Text);
    }

    [Fact]
    public void Read_ReadsATemplateAnEditorIsStillWriting()
    {
        // Arrange: a refused template read is recorded - and cached - as a broken template,
        // so the read must not be refusable by a save in flight.
        var full = Write("steps.yml", "steps:\n  - script: x\n");
        using var editor = new FileStream(full, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act.
        var document = new PipelineTemplates(_workspace).Read(full);

        // Assert.
        Assert.NotNull(document);
        Assert.Equal("steps:\n  - script: x\n", document.Text);
    }
}
