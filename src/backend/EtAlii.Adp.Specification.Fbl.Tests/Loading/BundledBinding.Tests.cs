using EtAlii.Adp.Specification.Fbl.Documents;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Loading;

/// <summary>
/// A module's bundled binding is loaded by its resource's name, or refused by name. This assembly embeds the vendored
/// timeline binding as a module embeds its own, so that no tool module is referenced for it (code-structure T287).
/// </summary>
public class BundledBindingTests
{
    [Fact]
    public void ABundledBinding_LoadsByItsResourceAndItsName()
    {
        // Act.
        var binding = BundledBinding.Load(typeof(BundledBindingTests).Assembly, "timeline.fbl", "timeline");

        // Assert.
        Assert.NotEmpty(binding.Elements);
    }

    [Fact]
    public void AnAbsentResource_IsRefused_NamingIt()
    {
        // Act.
        var refused = Assert.Throws<InvalidOperationException>(() => BundledBinding.Load(typeof(BundledBindingTests).Assembly, "absent.fbl"));

        // Assert.
        Assert.Contains("absent.fbl", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentBinding_IsRefused_NamingIt()
    {
        // Act.
        var refused = Assert.Throws<InvalidOperationException>(() => BundledBinding.Load(typeof(BundledBindingTests).Assembly, "timeline.fbl", "absent"));

        // Assert.
        Assert.Contains("no binding absent", refused.Message, StringComparison.Ordinal);
    }
}
