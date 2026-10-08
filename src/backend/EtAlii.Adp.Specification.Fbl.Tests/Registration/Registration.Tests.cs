using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Registration;

/// <summary>Finding the body (FBL §8.2), identities (FBL §8.6) and legacy sidecars (FBL §8.7).</summary>
public class RegistrationTests
{
    private static readonly Documents.FblBinding _timeline = RealFileCorpus.Binding("timeline.fbl", "timeline");

    [Fact]
    public void TheBodyIsTheSiblingWithTheRegistrationsBaseName()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        var registration = folder.Write("plan.adp", "generic/timeline\n");
        var body = folder.Write("plan.tml", "elements: []\n");

        // Act.
        var location = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), _timeline, folder.Path);

        // Assert.
        Assert.Equal(body, location.Path);
        Assert.True(location.Exists);
    }

    [Fact]
    public void AMissingBodyOpensAsMissing()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        var registration = folder.Write("plan.adp", "generic/timeline\nbody: gone.tml\n");

        // Act.
        var location = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), _timeline, folder.Path);

        // Assert.
        Assert.True(location.IsMissing);
        var finding = Assert.Single(location.Findings);
        Assert.Equal(FindingCodes.MissingBody, finding.Code);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Equal(("plan.adp", 2, 1), (finding.Location!.File, finding.Location.Line, finding.Location.Column));
    }

    [Fact]
    public void AMissingSiblingBodyIsReportedOnTheOriginLine()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        var registration = folder.Write("plan.adp", "generic/timeline\n");

        // Act.
        var location = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), _timeline, folder.Path);

        // Assert.
        Assert.True(location.IsMissing);
        var finding = Assert.Single(location.Findings);
        Assert.Equal(FindingCodes.MissingBody, finding.Code);
        Assert.Equal(1, finding.Location!.Line);
    }

    [Fact]
    public void AnExistingBodyHasNoFinding()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        var registration = folder.Write("plan.adp", "generic/timeline\n");
        folder.Write("plan.tml", "elements: []\n");

        // Act.
        var location = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), _timeline, folder.Path);

        // Assert.
        Assert.Empty(location.Findings);
    }

    [Theory]
    [InlineData("body: ../outside.tml\n")]
    [InlineData("body: /etc/passwd\n")]
    public void ABodyOutsideTheWorkspaceIsRefused(string header)
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        var registration = folder.Write("workspace/plan.adp", "generic/timeline\n" + header);

        // Act.
        var location = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), _timeline, Path.Combine(folder.Path, "workspace"));

        // Assert.
        Assert.NotNull(location.Refusal);
        Assert.Null(location.Path);
    }

    [Fact]
    public void ABodyReachedThroughALinkIsRefused()
    {
        // Arrange.
        using var folder = new TemporaryFolder();
        using var outside = new TemporaryFolder();
        outside.Write("plan.tml", "elements: []\n");
        Directory.CreateSymbolicLink(Path.Combine(folder.Path, "linked"), outside.Path);
        var registration = folder.Write("plan.adp", "generic/timeline\nbody: linked/plan.tml\n");

        // Act.
        var location = BodyLocator.Locate(registration, RegistrationDocument.Read(File.ReadAllBytes(registration)), _timeline, folder.Path);

        // Assert.
        Assert.NotNull(location.Refusal);
    }

    [Fact]
    public void AnIdentityIsStoredInOrderAfterTheLayout()
    {
        // Arrange.
        var registration = OpenRegistration.Open("wardley/map\r\nlayout:\r\n  a: 1 2\r\n"u8.ToArray());

        // Act.
        registration.Change(new ModelChange.Identify("Tea", "c2"));
        registration.Change(new ModelChange.Identify("Cup", "c1"));
        registration.Change(new ModelChange.Identify("Tea", "c3"));

        // Assert.
        Assert.Equal("wardley/map\r\nlayout:\r\n  a: 1 2\r\nidentities:\r\n  Cup: c1\r\n  Tea: c3\r\n", Encoding.UTF8.GetString(registration.Bytes));
        Assert.Equal("c3", registration.Document.IdentityMap()["Tea"]);
    }

    [Fact]
    public void ALegacyLayoutIsReadForItsViewIgnoringCase()
    {
        // Arrange.
        var sidecar = LegacySidecar.Open("{\n  \"SystemContext\": {\n    \"a\": { \"x\": 40, \"y\": 60.5 }\n  }\n}\n"u8.ToArray());

        // Act.
        var positions = sidecar.Positions("systemcontext");

        // Assert.
        Assert.Equal((40d, 60.5d), positions["a"]);
        Assert.Empty(sidecar.Positions("Containers"));
    }

    [Fact]
    public void ALegacyLayoutIsWrittenBySplicesAndUndone()
    {
        // Arrange.
        var original = "{\n  \"SystemContext\": {\n    \"a\": {\n      \"x\": 40,\n      \"y\": 60\n    }\n  }\n}\n";
        var sidecar = LegacySidecar.Open(Encoding.UTF8.GetBytes(original));

        // Act.
        sidecar.Change(s => s.PlanPlace("systemContext", "a", 41.25, 60));
        sidecar.Change(s => s.PlanPlace("SystemContext", "b", 10, 20.0004));

        // Assert: numbers replaced in place, a new member written as the file writes them.
        Assert.Equal("{\n  \"SystemContext\": {\n    \"a\": {\n      \"x\": 41.25,\n      \"y\": 60\n    },\n    \"b\": {\n      \"x\": 10,\n      \"y\": 20\n    }\n  }\n}\n", Encoding.UTF8.GetString(sidecar.Bytes));
        Assert.Equal((10d, 20d), sidecar.Positions("SystemContext")["b"]);
        Assert.IsType<UndoResult.Done>(sidecar.Undo());
        Assert.IsType<UndoResult.Done>(sidecar.Undo());
        Assert.Equal(original, Encoding.UTF8.GetString(sidecar.Bytes));
    }

    [Fact]
    public void LegacyIdentitiesAreReadAndWritten()
    {
        // Arrange.
        var sidecar = LegacySidecar.Open("{\r\n  \"Tea\": \"c1\"\r\n}\r\n"u8.ToArray());

        // Act.
        sidecar.Change(s => s.PlanIdentify("Cup", "c2"));

        // Assert.
        Assert.Equal("{\r\n  \"Tea\": \"c1\",\r\n  \"Cup\": \"c2\"\r\n}\r\n", Encoding.UTF8.GetString(sidecar.Bytes));
        Assert.Equal("c2", sidecar.Identities()["Cup"]);
    }

    [Fact]
    public void TheSidecarPathIsBesideTheBodyWithItsBaseName()
    {
        // Act.
        var path = LegacySidecar.PathFor("{base}.layout.json", Path.Combine("x", "bottling-mes.dsl"));

        // Assert.
        Assert.Equal(Path.Combine(Path.GetFullPath("x"), "bottling-mes.layout.json"), path);
    }

    [Fact]
    public void ABlockIsReadWithItsName()
    {
        // Act: two vendored registrations, one with an identities block and one with a layout block.
        var growth = RegistrationDocument.Read(File.ReadAllBytes(Path.Combine(Repository.Conformance, "registrations", "growth.adp")));
        var roadmap = RegistrationDocument.Read(File.ReadAllBytes(Path.Combine(Repository.Conformance, "registrations", "roadmap.adp")));

        // Assert.
        Assert.Equal("identities", growth.Identities!.Name);
        Assert.Null(growth.Layout);
        Assert.Equal("layout", roadmap.Layout!.Name);
        Assert.Null(roadmap.Identities);
    }
}
