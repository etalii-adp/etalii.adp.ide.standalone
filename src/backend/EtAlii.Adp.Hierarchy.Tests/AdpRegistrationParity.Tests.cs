using EtAlii.Adp.Diagram;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// <see cref="AdpRegistration"/> answers as the hierarchy's own readers do, over every registration
/// file the repository holds (code-structure FR-061, T173), so moving a caller onto it changes nothing
/// that caller sees.
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
    public void AdpRegistration_ReadsWhatTheHierarchyReads(string relativePath)
    {
        // Arrange.
        var path = IoPath.Combine(RepositoryPaths.Root, relativePath);

        // Act.
        var registration = AdpRegistration.Read(path);
        (string? body, string? view) = DiagramFilePair.ReadBodyAndViewHeaders(path);
        var layout = RegistrationLayout.Read(path);

        // Assert.
        Assert.NotNull(registration);
        Assert.Equal(DiagramFilePair.ReadMimeType(path), registration.MimeType);
        Assert.Equal(body, registration.Body);
        Assert.Equal(view, registration.View);
        Assert.Equal(
            layout.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value.X, entry.Value.Y)),
            registration.Layout.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => (entry.Key, entry.Value.X, entry.Value.Y)));
    }
}
