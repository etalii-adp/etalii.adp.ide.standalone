using Xunit;

namespace EtAlii.Adp.Tests;

public class DisposableTests
{
    [Fact]
    public void Disposing_RunsTheDelegateOnce()
    {
        // Arrange.
        var count = 0;
        var disposable = Disposable.From(() => count++);

        // Act.
        disposable.Dispose();
        disposable.Dispose();

        // Assert.
        Assert.Equal(1, count);
    }

    [Fact]
    public void NotDisposing_RunsNothing()
    {
        // Arrange.
        var count = 0;

        // Act.
        _ = Disposable.From(() => count++);

        // Assert.
        Assert.Equal(0, count);
    }
}
