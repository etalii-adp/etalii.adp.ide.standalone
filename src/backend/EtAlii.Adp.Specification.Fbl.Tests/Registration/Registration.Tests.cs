using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.Routing;
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

        // Assert: numbers replaced in place, a new member written as the file writes them, each number in its shortest form (FBL 0.11 §8.7).
        Assert.IsType<PlanResult.Planned>(moved);
        Assert.IsType<PlanResult.Planned>(added);
        Assert.Equal("{\n  \"SystemContext\": {\n    \"a\": {\n      \"x\": 41.25,\n      \"y\": 60\n    },\n    \"b\": {\n      \"x\": 10,\n      \"y\": 20.0004\n    }\n  }\n}\n", Encoding.UTF8.GetString(sidecar.Bytes));
        Assert.Equal((10d, 20.0004d), sidecar.Positions("SystemContext")["b"]);
        Assert.IsType<UndoResult.Done>(sidecar.Undo());
        Assert.IsType<UndoResult.Done>(sidecar.Undo());
        Assert.Equal(original, Encoding.UTF8.GetString(sidecar.Bytes));
    }

    /// <summary>A legacy identities file as the Wardley map's tool writes it (FBL 0.12 §8.7): an array of entries, one of a kind no rule declares last.</summary>
    private const string Identities = "[\n  {\n    \"id\": \"c1\",\n    \"kind\": \"component\",\n    \"key\": \"Tea\"\n  },\n  {\n    \"id\": \"c2\",\n    \"kind\": \"component\",\n    \"key\": \"Tea\\u001F2\\t\"\n  },\n  {\n    \"id\": \"n1\",\n    \"kind\": \"note\",\n    \"key\": \"Tea\"\n  },\n  {\n    \"id\": \"e1\",\n    \"kind\": \"evolution\",\n    \"key\": \"Tea\"\n  }\n]\n";

    [Fact]
    public void LegacyIdentitiesAreReadByKindAndKey_AndAnUndeclaredKindIsReported()
    {
        // Arrange.
        var sidecar = LegacySidecar.Open(Encoding.UTF8.GetBytes(Identities));

        // Act.
        var identities = sidecar.Identities();
        var findings = sidecar.IdentityFindings(new HashSet<string>(["component", "note"], StringComparer.Ordinal), "map.identities.json");

        // Assert: each entry under its kind and key, the ordinal's escapes resolved (FBL 0.12 §8.6, §8.7).
        Assert.Equal("c1", identities["component:Tea"]);
        Assert.Equal("c2", identities["component:Tea\u001f2\t"]);
        Assert.Equal("n1", identities["note:Tea"]);
        var finding = Assert.Single(findings);
        Assert.Equal(FindingCodes.StaleViewData, finding.Code);
        Assert.Equal(new SourceLocation("map.identities.json", 17, 3, 63), finding.Location);
    }

    [Fact]
    public void LegacyIdentitiesAreWrittenInTheirShape_AKeyMoved_AnEntryPruned_AndANewOneAdded()
    {
        // Arrange: the second Tea renamed to Cup, the note gone, and a new component.
        var sidecar = LegacySidecar.Open(Encoding.UTF8.GetBytes(Identities));
        LegacyIdentity[] wanted = [new("c1", "component", "Tea"), new("c2", "component", "Cup"), new("c3", "component", "Tea\u001f2\t")];

        // Act.
        var result = sidecar.Change(file => file.PlanIdentities(wanted, new HashSet<string>(["component", "note"], StringComparer.Ordinal)));

        // Assert: the key replaced, the note's entry removed with the separator before it, the new entry after the last item and
        // laid out as it is, with RFC 8785's escapes; the entry of the undeclared kind kept as written (FBL 0.12 §8.7).
        var planned = Assert.IsType<PlanResult.Planned>(result);
        Assert.Equal([SpliceOperation.ReplaceValue, SpliceOperation.RemoveEntry, SpliceOperation.InsertEntry], planned.Edit.Splices.Select(splice => splice.Operation));
        Assert.Equal("[\n  {\n    \"id\": \"c1\",\n    \"kind\": \"component\",\n    \"key\": \"Tea\"\n  },\n  {\n    \"id\": \"c2\",\n    \"kind\": \"component\",\n    \"key\": \"Cup\"\n  },\n  {\n    \"id\": \"e1\",\n    \"kind\": \"evolution\",\n    \"key\": \"Tea\"\n  },\n  {\n    \"id\": \"c3\",\n    \"kind\": \"component\",\n    \"key\": \"Tea\\u001f2\\t\"\n  }\n]\n", Encoding.UTF8.GetString(sidecar.Bytes));
        Assert.IsType<UndoResult.Done>(sidecar.Undo());
        Assert.Equal(Identities, Encoding.UTF8.GetString(sidecar.Bytes));
    }

    [Fact]
    public void LegacyIdentitiesThatHoldWhatIsWanted_WriteNothing()
    {
        // Arrange.
        var sidecar = LegacySidecar.Open(Encoding.UTF8.GetBytes(Identities));
        LegacyIdentity[] wanted = [new("c1", "component", "Tea"), new("c2", "component", "Tea\u001f2\t"), new("n1", "note", "Tea")];

        // Act.
        var result = sidecar.Change(file => file.PlanIdentities(wanted, new HashSet<string>(["component", "note"], StringComparer.Ordinal)));

        // Assert: an escape written in capitals is the same key.
        Assert.Empty(Assert.IsType<PlanResult.Planned>(result).Edit.Splices);
    }

    [Fact]
    public void ALegacyIdentitiesFileOfTheFormNoToolWrote_IsNotRead()
    {
        // Arrange: FBL 0.9's object from natural key to id (FBL 0.12 §8.7).
        var sidecar = LegacySidecar.Open("{\r\n  \"Tea\": \"c1\"\r\n}\r\n"u8.ToArray());

        // Act.
        var result = sidecar.Change(file => file.PlanIdentities([new LegacyIdentity("c1", "component", "Tea")], new HashSet<string>(["component"], StringComparer.Ordinal)));

        // Assert.
        Assert.Empty(sidecar.Identities());
        Assert.Equal("The identities file could not be read, so it is not written.", Assert.IsType<PlanResult.Refused>(result).Reason);
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

    [Theory]
    [InlineData("databricks/job\nbody: a.yml\nresource: job_b\n", "job_b")]
    [InlineData("databricks/job\r\nbody: a.yml\r\nresource:    job_b   \r\n", "job_b")]
    [InlineData("databricks/job\n\n\nresource: job_b\n", "job_b")]
    [InlineData("databricks/job\nbody: a.yml\n", null)]
    [InlineData("databricks/job\nbody: a.yml\nRESOURCE: job_b\n", null)]
    [InlineData("databricks/job\nbody: a.yml\nresource:job_b\n", null)]
    [InlineData("databricks/job\nbody: a.yml\nlayout:\n  task:x: 1 2\nresource: job_b\n", null)]
    public void TheResourceHeader_IsReadAsEveryHeaderIs(string text, string? resource)
    {
        // Arrange.
        var registration = OpenRegistration.Open(Encoding.UTF8.GetBytes(text));

        // Act.
        var read = registration.Resource;

        // Assert: by its exact key, after a colon and a space, in the header region only (FBL §8.1, 0.10 §8.2).
        Assert.Equal(resource, read);
    }
}
