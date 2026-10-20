using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>
/// The typed CEL contexts of DISL §12.3 and §8.4: the compiler holds a caller to a <see cref="DislContext"/>,
/// but not <see cref="DislContexts"/> to declaring each one, so a context added without its variables
/// and name fails here rather than when a definition first reaches it.
/// </summary>
public class DislContextsTests
{
    [Fact]
    public void EveryContext_IsDeclared()
    {
        // Act.
        var declared = DislContexts.All.Order();

        // Assert.
        Assert.Equal(Enum.GetValues<DislContext>(), declared);
    }
}
