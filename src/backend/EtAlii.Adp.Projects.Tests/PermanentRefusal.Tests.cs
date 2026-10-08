using System.Text.Json;
using System.Text.RegularExpressions;
using Grpc.Core;
using JetBrains.Annotations;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Projects.Tests;

/// <summary>
/// The status codes core refuses with for good, named once and checked against the golden fixture the
/// client's stream loop reads (backend-centralization R13.1, R13.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>The fixture states every status, not only the permanent ones.</b> A code missing from it would
/// leave the client to guess, so the fixture must list every gRPC code but OK, and each code's flag
/// must be what <see cref="PermanentRefusal"/> answers. A code added to, or dropped from, the one list
/// in core without the fixture is a red here; the client suite reads the same file.
/// </para>
/// <para>
/// <b>The list is only one place if every refusal uses it</b>, so the last test walks the backend's
/// sources for a status raised with a literal code rather than a name from the list.
/// </para>
/// </remarks>
public partial class PermanentRefusalTests
{
    // Lazy, so an unreadable fixture fails the tests that read it and not the source walk beside them.
    private static readonly Lazy<StatusFixture> LazyFixture = new(Load);

    private static StatusFixture Fixture => LazyFixture.Value;

    /// <summary>
    /// Statuses raised with a literal code that are deliberately not refusals of what was asked for,
    /// each with its reason. A stale entry fails too, so this list cannot outlive what it excuses.
    /// </summary>
    private static readonly Dictionary<string, string> NotARefusal = new(StringComparer.Ordinal)
    {
        ["src/backend/EtAlii.Adp.Authentication/Sessions/SessionInterceptor.cs"] =
            "Unauthenticated: the session interceptor's answer to any call without a valid token, met by signing in again.",
        ["src/backend/EtAlii.Adp.Diagram/WorkspaceService.cs"] =
            "Unavailable: OpenDiagram on a connection that is not open yet, which a reconnect racing a close produces and the client retries. " +
            "AlreadyExists: a stream id reused on one connection, a client defect rather than a refusal of what was asked.",
    };

    [Fact]
    public void TheFixtureLoaded_AndListsEveryStatusButOk()
    {
        // Every other test loops over the fixture, so one that parsed to nothing would let them pass
        // by checking nothing. This is the test that cannot pass that way.
        Assert.NotEmpty(Fixture.Reason);
        var expected = Enum.GetValues<StatusCode>().Where(code => code != StatusCode.OK).Select(code => (int)code).Order().ToList();
        var listed = Fixture.Statuses.Select(status => status.Code).Order().ToList();

        Assert.Equal(expected, listed);
        Assert.Contains(Fixture.Statuses, status => status.Permanent);
        Assert.Contains(Fixture.Statuses, status => !status.Permanent);
    }

    [Fact]
    public void EachStatusName_IsGrpcsCanonicalNameForItsCode()
    {
        var wrong = Fixture.Statuses
            .Where(status => status.Name != CanonicalName((StatusCode)status.Code))
            .Select(status => $"{status.Code}: '{status.Name}', expected '{CanonicalName((StatusCode)status.Code)}'")
            .ToList();

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void EachStatus_IsPermanentExactlyWhenCoreRefusesWithIt()
    {
        var disagreements = Fixture.Statuses
            .Where(status => status.Permanent != PermanentRefusal.IsPermanent((StatusCode)status.Code))
            .Select(status => $"{status.Name}: the fixture says {(status.Permanent ? "permanent" : "not permanent")}, PermanentRefusal says the opposite")
            .ToList();

        Assert.True(disagreements.Count == 0, string.Join(Environment.NewLine, disagreements));
    }

    [Fact]
    public void EveryRefusalCoreRaises_UsesANameFromTheOneList()
    {
        // Arrange: every production source file under src/backend and each module's backend.
        var root = RepositoryRoot();
        var sources = new[] { IoPath.Combine(root, "src", "backend"), IoPath.Combine(root, "src", "diagrams") }
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
            .Select(path => (Path: path, Relative: IoPath.GetRelativePath(root, path).Replace('\\', '/')))
            .Where(file => !file.Relative.Split('/').Any(segment => segment is "bin" or "obj" || segment.EndsWith(".Tests", StringComparison.Ordinal)))
            .ToList();

        // Act.
        var literal = new List<string>();
        var excused = new HashSet<string>(StringComparer.Ordinal);
        var named = 0;
        foreach ((string path, string relative) in sources)
        {
            var text = File.ReadAllText(path);
            named += NamedStatus().Count(text);
            foreach (Match match in LiteralStatus().Matches(text))
            {
                if (NotARefusal.ContainsKey(relative))
                {
                    excused.Add(relative);
                    continue;
                }

                var line = text[..match.Index].Count(character => character == '\n') + 1;
                literal.Add($"{relative}:{line} raises StatusCode.{match.Groups[1].Value}");
            }
        }

        // Assert.
        Assert.True(literal.Count == 0,
            "A status raised with a literal code rather than a name from PermanentRefusal - use the list, or add it to NotARefusal with a reason:" +
            Environment.NewLine + string.Join(Environment.NewLine, literal));
        var stale = NotARefusal.Keys.Except(excused, StringComparer.Ordinal).ToList();
        Assert.True(stale.Count == 0, "NotARefusal excuses files that no longer raise a literal status: " + string.Join(", ", stale));

        // And floors on what the walk saw, so a walk that read nothing, or a pattern that matches
        // nothing, cannot pass by finding no literal.
        Assert.True(sources.Count > 200, $"walked only {sources.Count} source files");
        Assert.True(named > 0, "found no refusal raised with a PermanentRefusal name, so the walk is not seeing core's refusals");
    }

    /// <summary>gRPC's canonical spelling: <c>FailedPrecondition</c> becomes <c>FAILED_PRECONDITION</c>.</summary>
    private static string CanonicalName(StatusCode code) => UpperBoundary().Replace(code.ToString(), "_$1").ToUpperInvariant();

    private static StatusFixture Load()
    {
        var path = IoPath.Combine(RepositoryRoot(), "src", "fixtures", "cross-tier", "permanent-statuses.json");
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<StatusFixture>(File.ReadAllText(path), options)
            ?? throw new InvalidOperationException($"{path} deserialised to nothing.");
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (Directory.Exists(IoPath.Combine(directory.FullName, "src", "fixtures", "cross-tier")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("The repository root (src/fixtures/cross-tier) was not found above the test binary.");
    }

    [GeneratedRegex(@"new\s+Status\s*\(\s*StatusCode\.(\w+)")]
    private static partial Regex LiteralStatus();

    [GeneratedRegex(@"new\s+Status\s*\(\s*PermanentRefusal\.\w+")]
    private static partial Regex NamedStatus();

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex UpperBoundary();

    private sealed record StatusFixture(string Reason, IReadOnlyList<StatusCase> Statuses);

    [UsedImplicitly] // Instantiated by System.Text.Json deserializing its StatusFixture.
    private sealed record StatusCase(int Code, string Name, bool Permanent);
}
