using Xunit;

namespace EtAlii.Adp.Tests;

public class CallbackDisposableTests
{
    [Fact]
    public void Dispose_InvokesTheCallback()
    {
        // Arrange.
        var count = 0;
        var disposable = new CallbackDisposable(() => count++);

        // Act.
        disposable.Dispose();

        // Assert.
        Assert.Equal(1, count);
    }

    [Fact]
    public void Dispose_CalledAgain_InvokesTheCallbackAgain()
    {
        // Arrange.
        // Non-idempotence is the specified behaviour, not an oversight: this type replaces
        // WardleyElementUnsubscriber and C4ElementUnsubscriber, which behaved exactly so.
        var count = 0;
        var disposable = new CallbackDisposable(() => count++);

        // Act.
        disposable.Dispose();
        disposable.Dispose();

        // Assert.
        Assert.Equal(2, count);
    }
}
