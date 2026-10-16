using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

/// <summary>What a body reads from its text is read once per text, and read again after an edit.</summary>
public class TextMemoTests
{
    [Fact]
    public void TheSameBytes_AreReadOnce()
    {
        // Arrange.
        var memo = new TextMemo<string>();
        var bytes = "a"u8.ToArray();
        var reads = 0;

        // Act.
        var first = memo.Of(bytes, () => $"read {++reads}");
        var second = memo.Of(bytes, () => $"read {++reads}");

        // Assert.
        Assert.Equal(("read 1", "read 1", 1), (first, second, reads));
    }

    [Fact]
    public void NewBytes_AreReadAgain_EvenWhenTheyHoldTheSameText()
    {
        // Arrange: an edit gives a body new bytes, whatever they hold.
        var memo = new TextMemo<string>();
        var reads = 0;

        // Act.
        var before = memo.Of("a"u8.ToArray(), () => $"read {++reads}");
        var after = memo.Of("a"u8.ToArray(), () => $"read {++reads}");

        // Assert.
        Assert.Equal(("read 1", "read 2"), (before, after));
    }
}
