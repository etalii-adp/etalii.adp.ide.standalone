using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Databricks.Tests;

/// <summary>
/// A body that does not exist yet opens as an empty document <b>quietly</b> — the missing-file
/// mechanism, not merely its outcome.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this guard was owed, and why it could not be written until now.</b> Deleting the
/// <c>File.Exists</c> check in <c>DatabricksDocumentStore.Load</c> leaves every existing test
/// green: <c>SharedDocumentReader</c> opens with <c>FileMode.Open</c>, the resulting
/// <c>FileNotFoundException</c> is an <c>IOException</c>, the catch below it swallows that and
/// returns the same empty document. The <em>only</em> observable difference is a warning
/// logged for what is an ordinary state — so the outcome cannot distinguish the two, and the
/// mechanism can only be observed through the log.
/// </para>
/// <para>
/// <b>Since backend-centralization task 6 that check is no longer this store's.</b> Opening
/// moved to the shared <c>DocumentLifecycle</c>, which keeps the <c>File.Exists</c> check and
/// also reads a <c>FileNotFoundException</c> as a missing body rather than a failure. The
/// guard below still holds this store to the outcome - a not-yet-created body opens empty and
/// logs nothing - whichever of the two produces it.
/// </para>
/// <para>
/// file-io-centralization recorded that as unwritable, because <c>LogCapture</c> lived in three
/// core test projects and no module one, and because a <c>private static readonly ILogger</c>
/// binds at type initialisation. Both halves were answered by the de-fork that put
/// <c>LogCapture</c> into every <c>*.Tests</c> project as source with a
/// <c>[ModuleInitializer]</c>, so the guard costs nothing now and the record was corrected.
/// </para>
/// <para>
/// It asserts on the <em>absence</em> of a warning rather than its text, so a reworded message
/// does not fail it — a guard over prose is the failure mode this repository has ruled out.
/// </para>
/// </remarks>
[Collection(LogCapture.Collection)]
public class DatabricksDocumentStoreMissingBodyTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-databricks-missing-" + Guid.NewGuid().ToString("N"));

    public DatabricksDocumentStoreMissingBodyTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ABodyThatDoesNotExistYet_OpensEmptyWithoutBeingReportedAsAFailure()
    {
        // Arrange: a path inside a real folder, deliberately never written.
        var path = IoPath.Combine(_workspace, "not-created-yet.yml");
        Assert.False(File.Exists(path));

        // Act.
        using var capture = LogCapture.Start();
        var entry = new DatabricksDocumentStore().GetOrLoad(path);

        // Assert: the outcome...
        Assert.NotNull(entry);
        Assert.Equal(string.Empty, entry.Document.Text);

        // ...and the mechanism that produced it. Without the File.Exists check the document is
        // still empty and this line is what tells the two apart: a not-yet-created body is an
        // ordinary state, and reporting it as a read failure trains the reader to ignore the log.
        Assert.Empty(capture.Warnings);
        Assert.Empty(capture.Errors);
    }

    [Fact]
    public void AnExistingBody_StillLoadsItsContent()
    {
        // The other half, so the guard above cannot be satisfied by a store that reads nothing.
        var path = IoPath.Combine(_workspace, "real.yml");
        File.WriteAllText(path, "bundle:\r\n  name: sample\r\n");

        using var capture = LogCapture.Start();
        var entry = new DatabricksDocumentStore().GetOrLoad(path);

        Assert.Contains("name: sample", entry.Document.Text, StringComparison.Ordinal);
        Assert.Empty(capture.Warnings);
    }
}
