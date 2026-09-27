using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// A reload that arrives between a command's edit and its save must not discard the edit.
/// </summary>
/// <remarks>
/// <para>
/// <b>The window is in <c>Save</c>'s shape.</b> A command does <c>GetOrLoad</c> - mutate the
/// document in place through <c>ReplaceLine</c> - <c>Save(path)</c>, and <c>Save(path)</c> looks
/// the entry up in the cache AGAIN rather than writing the document it was handed. <c>Reload</c>
/// replaces <c>_entries[path]</c> with a freshly parsed entry, so a reload landing between those
/// two steps means the object the command edited is no longer the object that gets written, and
/// the save persists the re-read file while returning <c>""</c> for success.
/// </para>
/// <para>
/// <b>Why that is worse than a refused save.</b> The command reports success, so its inverse goes
/// onto the undo stack for a change that never reached the disk. The user sees the edit revert,
/// and undoing it applies the inverse of an edit the file never received.
/// </para>
/// <para>
/// <b>Deterministic on purpose.</b> Calling <c>Reload</c> where the watcher's notification would
/// land is the same ordering without waiting for contention to produce it, so this fails every
/// time rather than one run in three.
/// </para>
/// </remarks>
public class C4DocumentStoreLostEditTests : IDisposable
{
    private const string Sample = """
        workspace "Sample" {
            model {
                s = softwareSystem "System" "Does the thing."
            }
            views {
                systemContext s "context" {
                    include *
                }
            }
        }
        """;

    private const string EditedLine = "        s = softwareSystem \"System\" \"Renamed by the command.\"";

    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-c4-lost-edit-" + Guid.NewGuid().ToString("N"));

    public C4DocumentStoreLostEditTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task ASaveAfterAReload_WritesTheEditRatherThanTheFileItJustReread()
    {
        // Arrange: a document loaded and edited in memory, exactly as C4Commands does it -
        // GetOrLoad, ReplaceLine, then ask the store to save.
        var path = IoPath.Combine(_workspace, "sample.dsl");
        await File.WriteAllTextAsync(path, Sample, TestContext.Current.CancellationToken);
        var store = new C4DocumentStore();
        var document = store.GetOrLoad(path);
        document.ReplaceLine(3, EditedLine);

        // The edit is in memory and nowhere else yet, which is this test's precondition.
        Assert.Contains("Renamed by the command.", document.ToText(), StringComparison.Ordinal);

        // Act: the notification for an EARLIER write of this same file lands now. The store's own
        // self-write guard is already cleared, so this is exactly what the reload bridge does.
        store.Reload(path);
        var error = store.Save(path, document);

        // Assert.
        Assert.Equal("", error.Error);
        var written = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        // The save reported success, so the edit it reported success for has to be in the file.
        Assert.Contains("Renamed by the command.", written, StringComparison.Ordinal);
    }
}
