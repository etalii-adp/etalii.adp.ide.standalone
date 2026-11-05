using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// Reading a requirement document (hierarchical-requirement FR-026, FR-027, SC-007; T042): every fault of the companion's
/// table "What reading reports" gives its one finding at its one bullet, or a read-only document, with every other entry
/// usable; a placeholder is numbered in memory only; and opening leaves both files' bytes as they were.
/// </summary>
public sealed class HierarchicalRequirementBodyTests
{
    private static string Fixture(string path) => IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, path);

    private static HierarchicalRequirementBody Read(string path) => HierarchicalRequirementBody.Read(File.ReadAllBytes(path), IoPath.GetFileName(path));

    [Fact]
    public void TheExample_ReadsWithExactlyItsMissingId()
    {
        // Act.
        var body = HierarchicalRequirementBody.Read(
            File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md")), "web-shop.md",
            File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.adp")), "web-shop.adp");

        // Assert: one finding, at REQ-NEW, which reads as the next number after the mark.
        var finding = Assert.Single(body.Document.Findings);
        Assert.Equal(("std.missingId", 44), (finding.Code, finding.Line));
        var numbered = Assert.Single(body.Document.Entries, entry => entry.Placeholder);
        Assert.Equal(("REQ-0013", "ITEM-0003"), (numbered.Id, numbered.ParentId));
        Assert.Equal(("ITEM-0007", "REQ-0012"), (body.Document.LastItem, body.Document.LastRequirement));
        Assert.Equal("", body.Document.ReadOnlyReason);
    }

    [Fact]
    public void EachFault_IsOneFindingAtItsBullet_AndEveryOtherEntryIsRead()
    {
        // Act.
        var document = Read(Fixture("hierarchical-requirement-faults/web-shop.md")).Document;

        // Assert: the findings of the fixture, in DESL's order: the reader's in reading order, then required, containment and mentions.
        Assert.Equal(
            [
                ("std.duplicateId", 15), ("std.missingId", 16), ("std.unreadableEntry", 18), ("std.missingId", 22), ("std.unreadableEntry", 28),
                ("std.required", 17), ("std.containment", 14), ("std.containment", 21), ("std.mentions", 13), ("std.mentions", 29),
            ],
            document.Findings.Select(finding => (finding.Code, finding.Line)));

        // And the entries around them are read: the duplicate under its written number, the placeholders numbered after the marks.
        Assert.Equal(
            ["ITEM-0001", "REQ-0001", "REQ-0002", "REQ-0003", "REQ-0002", "REQ-0009", "REQ-0004", "REQ-0006", "ITEM-0005", "REQ-0007"],
            document.Entries.Select(entry => entry.Id));
        Assert.Equal(["assessment/0", "assessment/1"], document.Assessments.Select(assessment => assessment.Id));
        Assert.Equal("", document.ReadOnlyReason);
    }

    [Theory]
    [InlineData("hierarchical-requirement-faults/no-envelope/web-shop.md", 1)]
    [InlineData("hierarchical-requirement-faults/not-utf8/cafe.md", 3)]
    public void AFileWithoutTheEnvelopeOrNotUtf8_IsReadOnly_WithOneFinding(string path, int line)
    {
        // Act.
        var document = Read(Fixture(path)).Document;

        // Assert.
        var finding = Assert.Single(document.Findings);
        Assert.Equal(("std.unparseable", line), (finding.Code, finding.Line));
        Assert.NotEqual("", document.ReadOnlyReason);
        Assert.Empty(document.Entries);
    }

    [Fact]
    public void ALaterDedVersion_IsReadOnly_WithTheReason_AndStillRead()
    {
        // Arrange.
        var text = File.ReadAllText(Fixture("hierarchical-requirement-read/library.md")).Replace("ded: 0.2", "ded: 0.9", StringComparison.Ordinal);

        // Act.
        var document = HierarchicalRequirementBody.Read(System.Text.Encoding.UTF8.GetBytes(text), "library.md").Document;

        // Assert.
        Assert.Contains("DED 0.9", document.ReadOnlyReason, StringComparison.Ordinal);
        Assert.Equal(14, document.Entries.Count);
    }

    [Fact]
    public void AnAssessmentsKindAndCheckbox_AreRead_InAnyCase()
    {
        // Act.
        var assessments = Read(Fixture("hierarchical-requirement-assessments/web-shop.md")).Document.Assessments;

        // Assert.
        Assert.Equal(
            [(AssessmentKind.Question, false), (AssessmentKind.Warning, true), (AssessmentKind.Remark, false), (AssessmentKind.Idea, true), (AssessmentKind.Question, false)],
            assessments.Select(assessment => (assessment.Kind, assessment.Resolved)));
        Assert.Equal("The gateway's session ends first, so ours never times out alone.", assessments[1].Detail);
    }

    [Fact]
    public void TheLists_AreTheRegistrations_ElseTheInitialAndTheImpliedMembers()
    {
        // Act.
        var stored = HierarchicalRequirementBody.Read(
            File.ReadAllBytes(Fixture("hierarchical-requirement-filters/requirements.md")), "requirements.md",
            File.ReadAllBytes(Fixture("hierarchical-requirement-filters/requirements.adp")), "requirements.adp").Document;
        var implied = Read(Fixture("hierarchical-requirement-tags/requirements.md")).Document;

        // Assert: a block the registration holds is the list; without one, DESL's initial members and then those the entries carry.
        Assert.Equal([new Tag("security", "red"), new Tag("ux", "blue")], stored.Tags);
        Assert.Equal(["security", "ux", "self-service", "privacy"], implied.Tags.Select(tag => tag.Name));
        Assert.All(implied.Tags, tag => Assert.Equal("default", tag.Colour));
        Assert.Equal(["functional", "non-functional", "technical"], implied.Categories.Select(category => category.Name));
        Assert.Equal(["proposed", "accepted", "rejected", "done"], implied.States.Select(state => state.Name));
        Assert.True(stored.Entries.Single(entry => entry.Id == "ITEM-0002").Collapsed);
    }

    [Fact]
    public void ARegistrationsFindings_AreItsOwn()
    {
        // Act.
        var document = HierarchicalRequirementBody.Read(
            File.ReadAllBytes(Fixture("hierarchical-requirement-collapsed/stale/lending.md")), "lending.md",
            File.ReadAllBytes(Fixture("hierarchical-requirement-collapsed/stale/lending.adp")), "lending.adp").Document;

        // Assert.
        var finding = Assert.Single(document.Findings);
        Assert.Equal(("fbl.stale-view-data", 4, true), (finding.Code, finding.Line, finding.InRegistration));
    }

    [Fact]
    public void Opening_LeavesBothFilesBytesAsTheyWere()
    {
        // Arrange.
        var body = File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"));
        var registration = File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.adp"));
        var bodyCopy = body.ToArray();
        var registrationCopy = registration.ToArray();

        // Act: the placeholder is numbered, in memory only.
        var read = HierarchicalRequirementBody.Read(body, "web-shop.md", registration, "web-shop.adp");

        // Assert.
        Assert.Equal(bodyCopy, read.Body.Bytes);
        Assert.Equal(registrationCopy, read.Registration!.Bytes);
        Assert.Equal(bodyCopy, body);
        Assert.False(read.Body.CanUndo);
    }
}
