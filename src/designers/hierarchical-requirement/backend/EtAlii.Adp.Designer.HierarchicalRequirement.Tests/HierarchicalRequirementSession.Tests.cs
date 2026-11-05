using EtAlii.Adp.Files;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// One open requirement document on one connection, as a hierarchy surface (spec 029 FR-010, FR-031; US1 scenario 5):
/// the ids its model gives by the contract, collapsing kept in the registration alone, and a document of a later DED
/// version opened read-only with the reason.
/// </summary>
public sealed class HierarchicalRequirementSessionTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-requirement-session-").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still be letting go; the temp folder is the system's to clear.
        }
    }

    /// <summary>The example's body, copied into the test's folder without its registration, as an agent would leave it.</summary>
    private string CopyBody(Func<string, string>? change = null)
    {
        var text = File.ReadAllText(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"));
        var path = IoPath.Combine(_root, "web-shop.md");
        File.WriteAllText(path, change is null ? text : change(text));
        return path;
    }

    /// <summary>The line ending the example was checked out with, so an inserted line keeps it.</summary>
    private static string NewlineOf(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static HierarchicalRequirementSession Open(string body) => new(body, registrationPath: null, new FileWriter());

    [Fact]
    public async Task ANumberedEntry_IsNamedByItsNumber_AndAPlaceholder_ByItsLine()
    {
        // Arrange: a requirement added by an agent, with the placeholder in place of a number, beside the one the example has.
        var body = CopyBody(text => text.Replace(
            "  * REQ-0001 A customer finds",
            $"  * REQ-NEW A customer keeps a wish list.{NewlineOf(text)}  * REQ-0001 A customer finds",
            StringComparison.Ordinal));
        var line = Array.FindIndex(await File.ReadAllLinesAsync(body, TestContext.Current.CancellationToken), text => text.Contains("REQ-NEW A customer keeps", StringComparison.Ordinal)) + 1;
        await using var session = Open(body);

        // Act.
        var model = session.Model();

        // Assert: the numbered ones by their number, the new one by the line of its bullet.
        Assert.Equal("", model.ReadOnlyReason);
        Assert.Equal("Web shop", model.Title);
        Assert.Contains(model.Nodes, node => node is { Id: "ITEM-0001", Number: "ITEM-0001", Role: HierarchySurfaceRoles.Level });
        Assert.Contains(model.Nodes, node => node is { Id: "REQ-0001", ParentId: "ITEM-0001", Role: HierarchySurfaceRoles.Entry });
        var added = Assert.Single(model.Nodes, node => node.Title == "A customer keeps a wish list.");
        Assert.Equal($"line/{line}", added.Id);
        Assert.Equal("ITEM-0001", added.ParentId);
    }

    [Fact]
    public async Task Collapsing_WritesOnlyTheRegistration_CreatingIt_AndIsKeptForTheNextOpening()
    {
        // Arrange: no registration yet.
        var body = CopyBody();
        var registration = IoPath.ChangeExtension(body, ".adp");
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        await using (var session = Open(body))
        {
            Assert.False(session.Model().Nodes.Single(node => node.Id == "REQ-0001").Collapsed);

            // Act.
            var refusal = session.Gesture(ShortGuid.NewShortGuid(), new HierarchySurfaceGesture("toggleCollapsed", "REQ-0001", new Dictionary<string, string>()));
            await session.Writes;

            // Assert: accepted, the registration created with the fold and nothing else of the body touched.
            Assert.Equal("", refusal);
            Assert.True(File.Exists(registration));
            var written = await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken);
            Assert.StartsWith("etalii/hierarchical-requirement", written, StringComparison.Ordinal);
            Assert.Contains("collapsed:", written, StringComparison.Ordinal);
            Assert.Contains("REQ-0001: yes", written, StringComparison.Ordinal);
            Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
            Assert.True(session.Model().Nodes.Single(node => node.Id == "REQ-0001").Collapsed);
        }

        // Act: the document opened again.
        await using var reopened = Open(body);
        // Assert: collapsed as it was left.
        Assert.True(reopened.Model().Nodes.Single(node => node.Id == "REQ-0001").Collapsed);

        // Act: expanded again.
        Assert.Equal("", reopened.Gesture(ShortGuid.NewShortGuid(), new HierarchySurfaceGesture("toggleCollapsed", "REQ-0001", new Dictionary<string, string>())));
        await reopened.Writes;

        // Assert: the entry gone from the block, the body still untouched.
        Assert.DoesNotContain("REQ-0001: yes", await File.ReadAllTextAsync(registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.False(reopened.Model().Nodes.Single(node => node.Id == "REQ-0001").Collapsed);
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnEntryWithoutANumber_IsNotCollapsedInTheDocument()
    {
        // Arrange.
        var body = CopyBody(text => text.Replace("  * REQ-0001 A customer finds", $"  * REQ-NEW A wish list.{NewlineOf(text)}  * REQ-0001 A customer finds", StringComparison.Ordinal));
        await using var session = Open(body);
        var added = session.Model().Nodes.Single(node => node.Title == "A wish list.");

        // Act.
        var refusal = session.Gesture(ShortGuid.NewShortGuid(), new HierarchySurfaceGesture("toggleCollapsed", added.Id, new Dictionary<string, string>()));
        await session.Writes;

        // Assert: refused with the reason, and nothing written.
        Assert.Equal("REQ-NEW has no number of its own yet, so it is collapsed on this page only.", refusal);
        Assert.False(File.Exists(IoPath.ChangeExtension(body, ".adp")));
    }

    [Fact]
    public async Task ADocumentOfALaterVersion_OpensReadOnly_WithTheReason()
    {
        // Arrange.
        var body = CopyBody(text => text.Replace("<!-- ded: 0.2 ", "<!-- ded: 0.3 ", StringComparison.Ordinal));
        await using var session = Open(body);

        // Act.
        var model = session.Model();
        var refusal = session.Gesture(ShortGuid.NewShortGuid(), new HierarchySurfaceGesture("toggleCollapsed", "REQ-0001", new Dictionary<string, string>()));
        await session.Writes;

        // Assert: shown, with the reason, and nothing it asks is done.
        Assert.Contains("0.3", model.ReadOnlyReason, StringComparison.Ordinal);
        Assert.NotEmpty(model.Nodes);
        Assert.Equal(model.ReadOnlyReason, refusal);
        Assert.False(File.Exists(IoPath.ChangeExtension(body, ".adp")));
    }
}
