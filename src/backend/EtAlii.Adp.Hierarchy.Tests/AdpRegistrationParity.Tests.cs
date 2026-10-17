using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// <see cref="AdpRegistration"/>'s layout answers as <see cref="RegistrationLayout"/> does, over every
/// registration file the repository holds (code-structure FR-061, T173), so moving the layout onto it
/// (T175) changes nothing a module sees. The MIME line and the headers needed the same check until the
/// hierarchy read them through the type itself (T174); it would now compare the type with itself.
/// </summary>
public class AdpRegistrationParityTests
{
    private static readonly string[] _skipped = ["bin", "obj", "node_modules"];

    private static string Source => IoPath.Combine(RepositoryPaths.Root, "src");

    public static TheoryData<string> Registrations()
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(Source, "*.adp", SearchOption.AllDirectories)
                     .Where(path => !IoPath.GetRelativePath(Source, path).Split(IoPath.DirectorySeparatorChar).Any(_skipped.Contains))
                     .Order(StringComparer.Ordinal))
        {
            data.Add(IoPath.GetRelativePath(RepositoryPaths.Root, path));
        }

        return data;
    }

    [Fact]
    public void TheCorpus_HoldsRegistrations()
    {
        // Assert: a parity over no files would pass for any parser.
        Assert.True(Registrations().Count > 100, $"Found {Registrations().Count} registration files under src/.");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void AdpRegistration_ReadsTheLayoutRegistrationLayoutReads(string relativePath)
    {
        // Arrange.
        var path = IoPath.Combine(RepositoryPaths.Root, relativePath);

        // Act.
        var registration = AdpRegistration.Read(path);
        var layout = RegistrationLayout.Read(path);

        // Assert.
        Assert.NotNull(registration);
        Assert.Equal(
            layout.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value.X, entry.Value.Y)),
            registration.Layout.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value.X, entry.Value.Y)));
    }
}
