using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The description reader against a fixture cache laid out exactly as NuGet lays out the real
/// one - lowercased folders, a lowercased <c>.nuspec</c>, a namespaced document - so that the
/// tests fail for the same reasons the machine's own cache would.
/// </summary>
public class PackageDescriptionReaderTests
{
    private static PackageDescriptionReader Reader() =>
        new(Path.Combine(AppContext.BaseDirectory, "Fixtures", "cache"));

    [Fact]
    public void Read_TakesTheDescriptionFromACachedNuspec()
    {
        // Arrange, act.
        var description = Reader().Read("Cached", ["1.0.0"]);

        // Assert.
        Assert.Equal("Simple .NET logging with fully-structured events", description);
    }

    [Fact]
    public void TheCachePath_IsLowercased_BecauseThatIsHowNuGetRestores()
    {
        // NuGet lowercases the folder and the .nuspec name when it restores, whatever casing
        // the .csproj wrote. A reader taking the declared casing finds nothing on a
        // case-sensitive filesystem and calls every package uncached.
        //
        // This asserts the CONSTRUCTED PATH rather than the read, and that is deliberate. The
        // behavioural form of this test - read "CACHED" and expect the description - passes
        // against the defect on Windows, because NTFS is case-insensitive and finds the file
        // either way. It was written that way first, sabotaged, and seen to PASS: a test that
        // is green on this machine whether or not the code is right. Asserting the path is the
        // only form of the guard that fails here for the reason it exists.

        // Arrange, act.
        var path = PackageDescriptionReader.NuspecPathFor(Path.Combine("root"), "Serilog.Sinks.File", "4.4.0");

        // Assert.
        Assert.Equal(Path.Combine("root", "serilog.sinks.file", "4.4.0", "serilog.sinks.file.nuspec"), path);
    }

    [Fact]
    public void Read_FindsAPackageWhateverCasingTheReferenceUsed()
    {
        // The behavioural half, kept for what it does prove - the reader does not reject a
        // differently-cased id outright - while the path test above carries the guard.

        // Arrange, act.
        var description = Reader().Read("CACHED", ["1.0.0"]);

        // Assert.
        Assert.Equal("Simple .NET logging with fully-structured events", description);
    }

    [Fact]
    public void Read_APackageThatWasNeverRestored_IsAbsenceRatherThanAnError()
    {
        // Requirement 5.3, and the whole cost of the cache-only decision: never an error, never
        // a wait, never an empty diagram - just no description.

        // Arrange, act.
        var description = Reader().Read("NeverRestored", ["1.0.0"]);

        // Assert.
        Assert.Null(description);
    }

    [Fact]
    public void Read_AnEmptyDescription_IsEmptyRatherThanAbsent()
    {
        // Absence and emptiness are different answers and the grid renders them differently
        // (Requirement 4.4). A reader that returned null for both would erase a distinction the
        // requirement asks for - and this is the one case where the difference is visible.

        // Arrange, act.
        var description = Reader().Read("EmptyDescription", ["1.0.0"]);

        // Assert.
        Assert.NotNull(description);
        Assert.Equal("", description);
    }

    [Fact]
    public void Read_TriesEveryVersionOffered_BecauseADescriptionDescribesThePackageNotTheRelease()
    {
        // A package in version conflict has several versions and may be cached at only one of
        // them. Giving up after the first would report no description for a package that is
        // sitting in the cache.

        // Arrange, act.
        var description = Reader().Read("Cached", ["9.9.9", "1.0.0"]);

        // Assert.
        Assert.Equal("Simple .NET logging with fully-structured events", description);
    }

    [Fact]
    public void Read_AnAbsentCacheFolderEntirely_IsAbsenceRatherThanAThrow()
    {
        // A machine that has never restored anything, or a redirected NUGET_PACKAGES pointing
        // somewhere that does not exist yet.

        // Arrange.
        var reader = new PackageDescriptionReader(Path.Combine(AppContext.BaseDirectory, "Fixtures", "no-such-cache"));

        // Act.
        var description = reader.Read("Cached", ["1.0.0"]);

        // Assert.
        Assert.Null(description);
    }

    [Fact]
    public void TheDefaultCacheFolder_HonoursNugetPackages_SoARedirectedCacheIsFound()
    {
        // A build agent and a repository-local cache both redirect through this variable; a
        // reader hard-coding the per-user path would silently answer for the wrong cache.

        // Arrange.
        var original = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        try
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", Path.Combine("X:", "redirected"));

            // Act, assert.
            Assert.Equal(Path.Combine("X:", "redirected"), PackageDescriptionReader.DefaultGlobalPackagesFolder());
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", original);
        }
    }
}
