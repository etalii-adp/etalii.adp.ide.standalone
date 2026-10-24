using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// The one clock (code-structure FR-051): the real host serves the system clock, and a test
/// project has the fake one to advance in its place.
/// </summary>
public class HostClockTests(HostFixture host) : IClassFixture<HostFixture>
{
    [Fact]
    public void TheHost_ServesTheSystemClock()
    {
        // Act: building the server is what runs Program.cs, and with it the registration.
        using var _ = host.CreateChannel();

        // Assert.
        Assert.Same(TimeProvider.System, host.Services.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void ATestProject_HasTheFakeClock_ThroughTestSupport()
    {
        // Arrange.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

        // Act.
        clock.Advance(TimeSpan.FromMinutes(5));

        // Assert.
        Assert.Equal(new DateTimeOffset(2026, 10, 15, 12, 5, 0, TimeSpan.Zero), clock.GetUtcNow());
    }
}
