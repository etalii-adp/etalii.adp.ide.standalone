using Xunit;

using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.Modules.Tests;

/// <summary>
/// The open folders of a diagram drawn from a whole folder, shared and closed by claim count once for every
/// such module (code-structure T294): one reading per registration, kept open while a session claims it and
/// closed when the last claim goes. A stand-in folder records its disposal, so no folder is read.
/// </summary>
public sealed class OpenFoldersTests : IDisposable
{
    private static readonly string Root = IoPath.GetFullPath(IoPath.Combine(IoPath.GetTempPath(), "open-folders", "root"));

    private readonly List<Folder> _opened = [];
    private readonly OpenFolders<Folder> _folders;

    public OpenFoldersTests()
    {
        _folders = new OpenFolders<Folder>(
            (folder, registration) =>
            {
                var opened = new Folder(folder, registration);
                _opened.Add(opened);
                return opened;
            },
            (opened, settled) => opened.Settled += settled);
    }

    public void Dispose() => _folders.Dispose();

    [Fact]
    public void GetOrLoad_OpensOnce_AndHandsTheSameFolderBack()
    {
        // Act.
        var first = _folders.GetOrLoad(Root, null);
        var second = _folders.GetOrLoad(Root, null);

        // Assert.
        Assert.Same(first, second);
        Assert.Single(_opened);
    }

    [Fact]
    public void ReleasingOneOfTwoClaims_KeepsTheFolderOpen_AndReleasingBothClosesIt()
    {
        // Arrange.
        var held = _folders.Acquire(Root, null);
        _folders.Acquire(Root, null);

        // Act and assert, step by step: a second connection closing must not close the folder under the first.
        _folders.Release(Root, null);
        Assert.False(held.Disposed);
        Assert.Same(held, _folders.GetOrLoad(Root, null));

        _folders.Release(Root, null);
        Assert.Equal(1, held.Disposals);
        var again = _folders.GetOrLoad(Root, null);
        Assert.NotSame(held, again);
        Assert.False(again.Disposed);
    }

    [Fact]
    public void AFolderNobodyClaimed_StaysOpen_UntilAReleaseClosesIt()
    {
        // Arrange.
        var asked = _folders.GetOrLoad(Root, null);

        // Act and assert.
        Assert.False(asked.Disposed);
        _folders.Release(Root, null);
        Assert.True(asked.Disposed);
    }

    [Fact]
    public void ReleasingAFolderNobodyOpened_IsNotAnError()
    {
        // Act and assert.
        _folders.Release(Root, null);
        _folders.Release(IoPath.Combine(Root, "never-loaded"), null);
        Assert.Empty(_opened);
    }

    [Fact]
    public void AFolderIsKeyedByItsRegistration()
    {
        // Arrange: the registration holds the positions, so the folder body watches the file each session writes to.
        var registration = IoPath.Combine(Root, "chart.adp");

        // Act.
        var with = _folders.GetOrLoad(Root, registration);
        var without = _folders.GetOrLoad(Root, null);

        // Assert.
        Assert.NotSame(with, without);
        Assert.Same(with, _folders.GetOrLoad(IoPath.Combine(Root, "elsewhere"), registration));
        Assert.Equal((Root, registration), (with.Path, with.Registration));
    }

    [Fact]
    public void AWatch_HearsItsFolder_AndNothingOnceItIsLetGo()
    {
        // Arrange.
        var opened = _folders.GetOrLoad(Root, null);
        var other = _folders.GetOrLoad(IoPath.Combine(Root, "other"), null);
        var heard = new List<Folder>();
        var watch = _folders.Watch(Root, null, heard.Add);

        // Act: heard, a neighbour's change not heard, then let go - twice, which is harmless.
        opened.Settle();
        other.Settle();
        watch.Dispose();
        watch.Dispose();
        opened.Settle();

        // Assert.
        Assert.Equal([opened], heard);
    }

    [Fact]
    public void Dispose_ClosesEveryFolderOnce_AndAgainIsHarmless()
    {
        // Arrange.
        var held = _folders.Acquire(Root, null);
        var asked = _folders.GetOrLoad(IoPath.Combine(Root, "other"), null);

        // Act.
        _folders.Dispose();
        _folders.Dispose();

        // Assert.
        Assert.Equal((1, 1), (held.Disposals, asked.Disposals));
    }

    [Fact]
    public void NoFolder_IsRefused()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => _folders.GetOrLoad(null!, null));
        Assert.Throws<ArgumentException>(() => _folders.Acquire(" ", null));
        Assert.Throws<ArgumentNullException>(() => _folders.Watch(Root, null, null!));
        Assert.Empty(_opened);
    }

    private sealed class Folder(string path, string? registration) : IDisposable
    {
        public event Action? Settled;

        public string Path { get; } = path;

        public string? Registration { get; } = registration;

        public int Disposals { get; private set; }

        public bool Disposed => Disposals > 0;

        public void Settle() => Settled?.Invoke();

        public void Dispose() => Disposals++;
    }
}
