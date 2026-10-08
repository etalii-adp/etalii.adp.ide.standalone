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
        folder.Link("linked", outside.Path);
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
        var moved = sidecar.Change(s => s.PlanPlace("systemContext", "a", 41.25, 60));
        var added = sidecar.Change(s => s.PlanPlace("SystemContext", "b", 10, 20.0004));

        // Assert: numbers replaced in place, a new member written as the file writes them.
        Assert.IsType<PlanResult.Planned>(moved);
        Assert.IsType<PlanResult.Planned>(added);
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
        var result = sidecar.Change(s => s.PlanIdentify("Cup", "c2"));

        // Assert.
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("{\r\n  \"Tea\": \"c1\",\r\n  \"Cup\": \"c2\"\r\n}\r\n", Encoding.UTF8.GetString(sidecar.Bytes));
        Assert.Equal("c2", sidecar.Identities()["Cup"]);
    }

    [Fact]
    public void AnUnreadableSidecarRefusesAChangeAndIsNotWritten()
    {
        // Arrange.
        var original = "[ \"not an object\" ]\n";
        var sidecar = LegacySidecar.Open(Encoding.UTF8.GetBytes(original));

        // Act.
        var result = sidecar.Change(s => s.PlanPlace("SystemContext", "a", 1, 2));

        // Assert: the refusal is the sentence a host shows (FBL §8.7).
        Assert.Equal("The layout file could not be read, so it is not written.", Assert.IsType<PlanResult.Refused>(result).Reason);
        Assert.Equal(original, Encoding.UTF8.GetString(sidecar.Bytes));
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

    [Fact]
    public void AnEphemeralElementsPositionIsNotStored()
    {
        // Arrange: a vendored registration with a layout block, and a reading that marks one id ephemeral.
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Conformance, "registrations", "roadmap.adp"));
        var registration = OpenRegistration.Open(bytes);
        registration.IsEphemeral = id => id == "discovery";

        // Act.
        var ephemeral = registration.Change(new ModelChange.Place("discovery", 130, 70));

        // Assert: refused and nothing written, while another id is still placed.
        Assert.Contains("'discovery'", Assert.IsType<PlanResult.Refused>(ephemeral).Reason);
        Assert.Equal(bytes, registration.Bytes);
        Assert.IsType<PlanResult.Planned>(registration.Change(new ModelChange.Place("launch", 650, 120)));
        Assert.Equal("generic/timeline\r\nbody: roadmap.tml\r\nlayout:\r\n  discovery: 120 60\r\n  launch: 650 120\r\n", Encoding.UTF8.GetString(registration.Bytes));
    }

    [Fact]
    public void AnIdentityTheReadingNoLongerHasIsRemovedAtTheNextStore()
    {
        // Arrange: a vendored registration with two identities, of which the reading still knows one.
        var registration = OpenRegistration.Open(File.ReadAllBytes(Path.Combine(Repository.Conformance, "registrations", "growth.adp")));
        registration.KnownKeys = new HashSet<string>(["component Customer", "component Tea"], StringComparer.Ordinal);

        // Act.
        var result = registration.Change(new ModelChange.Identify("component Tea", "t1"));

        // Assert: the stale "component Web shop" entry goes in the same edit (FBL §8.6).
        Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal("wardley/map\r\nbody: growth.owm\r\nidentities:\r\n  component Customer: 3k2m9x0q1v7b8n4c5d6f7g8h9\r\n  component Tea: t1\r\n", Encoding.UTF8.GetString(registration.Bytes));
        Assert.False(registration.Document.IdentityMap().ContainsKey("component Web shop"));
    }
}
