using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// The sidecar's modify path is read-modify-write over the whole file, so what happens when
/// the read half fails decides whether one view's drag can destroy every other view's
/// arrangement. A read merely refused this instant must refuse the modify (the positions
/// still exist - Requirement 8.3 says they survive); content that is already nonsense may be
/// replaced (nothing that still exists is lost). Requirement 3.6's tolerance belongs to the
/// open path alone.
/// </summary>
public class C4LayoutSidecarContendedWriteTests : IDisposable
{
    private readonly string _root;
    private readonly string _bodyPath;
    private readonly C4LayoutSidecar _sidecar = new();

    public C4LayoutSidecarContendedWriteTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Diagram.C4.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _bodyPath = IoPath.Combine(_root, "model.dsl");
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    [Fact]
    public void Write_WhileTheSidecarCannotBeRead_RefusesRatherThanWipingOtherViews()
    {
        // Arrange: one view's arrangement is on disk.
        _sidecar.Write(_bodyPath, "context", "a", new C4SidecarPosition(100, 200));

        // A holder that blocks reads but not writes - FileShare.Write without Read - which is
        // exactly the shape that used to make the destructive path fire: the modify's read was
        // refused, came back empty, and the write then succeeded over the full file.
        using (new FileStream(C4LayoutSidecar.PathFor(_bodyPath), FileMode.Open, FileAccess.Read, FileShare.Write))
        {
            // Act: another view's drag lands while the read is refused.
            _sidecar.Write(_bodyPath, "containers", "b", new C4SidecarPosition(300, 400));
        }

        // Assert: the first view's arrangement survived (Requirement 8.3). The contended
        // drag's own position is the acceptable loss - the next drag lands it.
        Assert.Equal(new C4SidecarPosition(100, 200), _sidecar.Read(_bodyPath, "context")["a"]);
    }

    [Fact]
    public void Write_OverANonsenseSidecar_StillProceeds()
    {
        // Arrange: content that is already unrecoverable - replacing it costs nothing that
        // still exists, and keeps a corrupt sidecar self-healing on the next drag.
        File.WriteAllText(C4LayoutSidecar.PathFor(_bodyPath), "{ not json");

        // Act.
        _sidecar.Write(_bodyPath, "context", "a", new C4SidecarPosition(1, 2));

        // Assert.
        Assert.Equal(new C4SidecarPosition(1, 2), _sidecar.Read(_bodyPath, "context")["a"]);
    }
}
