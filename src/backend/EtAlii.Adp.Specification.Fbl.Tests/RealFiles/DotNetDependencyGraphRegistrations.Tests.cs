using System.Text;
using EtAlii.Adp.Diagram;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.RealFiles;

/// <summary>
/// The fixtures of finding B14 of dotnet-dependency-graph-disl-fbl, run before a move of the .NET
/// dependency graph is written through the library (its task 8, Requirement 5.2): every registration
/// of the graph's corpus written back through <see cref="OpenRegistration"/> with nothing moved, that
/// is with every entry of its <c>layout:</c> block placed again at the position it already holds, and
/// compared byte for byte with the file. The registration whose ids order differently in UTF-8 and
/// UTF-16 is among them.
/// </summary>
/// <remarks>
/// Core's writer is held to the same files beside it, so that a difference between the two shows here
/// as one of them failing rather than as a changed transcript later.
/// </remarks>
public sealed class DotNetDependencyGraphRegistrationsTests
{
    private static string Module { get; } = RepositoryPaths.Under("src", "diagrams", "dotnet-dependency-graph");

    public static TheoryData<string> Registrations()
    {
        var data = new TheoryData<string>();
        foreach (var file in All()) data.Add(file);
        return data;
    }

    [Fact]
    public void TheCorpus_HoldsTheRegistrationWhoseIdsOrderDifferentlyInUtf8AndUtf16()
    {
        // Act.
        var files = All();

        // Assert: a byte-for-byte check over no file, or without the one B14 is about, shows nothing.
        Assert.Contains("backend/EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests/Fixtures/registration-order/Order.adp", files);
        Assert.True(files.Count >= 5, $"{files.Count} registrations were found in the .NET dependency graph's corpus; 5 were there when this was written.");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void TheLibrary_WritesTheRegistrationBackByteIdentical_WhenNothingMoved(string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(Path.Combine(Module, file));
        var registration = OpenRegistration.Open(bytes);

        // Act.
        Assert.IsType<PlanResult.Planned>(registration.Change(new ModelChange.Save()));
        foreach ((string id, RegistrationPosition position) in Layout(bytes))
        {
            Assert.IsType<PlanResult.Planned>(registration.Change(new ModelChange.Place(id, position.X, position.Y)));
        }

        // Assert.
        Assert.True(bytes.AsSpan().SequenceEqual(registration.Bytes), Difference(file, bytes, registration.Bytes));
        Assert.False(registration.CanUndo, $"{file}: placing every entry where it is recorded an edit.");
    }

    [Theory]
    [MemberData(nameof(Registrations))]
    public void Core_WritesTheRegistrationBackByteIdentical_WhenNothingMoved(string file)
    {
        // Arrange.
        var bytes = File.ReadAllBytes(Path.Combine(Module, file));
        var registration = AdpRegistration.Parse(Encoding.UTF8.GetString(bytes));

        // Act.
        var written = Encoding.UTF8.GetBytes(registration.WithLayout(registration.Layout));

        // Assert.
        Assert.True(bytes.AsSpan().SequenceEqual(written), Difference(file, bytes, written));
    }

    /// <summary>Every registration of the .NET dependency graph, its fixtures and its examples, relative to its folder.</summary>
    private static List<string> All() =>
        Directory.EnumerateFiles(Module, "*.adp", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Module, path).Replace('\\', '/'))
            .Where(path => !path.Split('/').Any(segment => segment is "bin" or "obj" or "node_modules"))
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The layout block as written, entry by entry.</summary>
    private static IReadOnlyDictionary<string, RegistrationPosition> Layout(byte[] bytes) =>
        AdpRegistration.Parse(Encoding.UTF8.GetString(bytes)).Layout;

    /// <summary>The first byte at which the two differ, with the text around it.</summary>
    private static string Difference(string file, byte[] expected, byte[] actual)
    {
        var at = expected.AsSpan().CommonPrefixLength(actual);
        return $"{file} differs from byte {at}.\nFile:    {Escape(expected, at)}\nWritten: {Escape(actual, at)}";
    }

    private static string Escape(byte[] bytes, int at)
    {
        // Back to the start of a character, so the excerpt never opens inside one.
        var start = Math.Max(0, at - 16);
        while (start > 0 && start < bytes.Length && (bytes[start] & 0xC0) == 0x80) start--;
        var text = Encoding.UTF8.GetString(bytes, start, Math.Min(bytes.Length - start, 48));
        return text.Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
