using EtAlii.Adp.Documents;
using EtAlii.Adp.History;
using EtAlii.Adp.TestSupport;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// Delete under nesting (adp-file-nesting Requirement 6): a subject's delete cascades to its
/// registrations - after the confirmation said how many - and a registration's delete never
/// takes the subject or a shared body with it.
/// </summary>
public class DeleteEntryNestingTests : IDisposable
{
    private static readonly DiagramDefinition Mindmap = new(new DiagramOrigin("freeplane", "mindmap"), "Mind map", Extension: ".mm");

    private readonly string _root;
    private readonly IHistoryStack _history;

    public DeleteEntryNestingTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _history = TestHistory.Create(_root, out _, Mindmap);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string name, string content)
    {
        var path = IoPath.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task DeletingASubject_CascadesToEveryRegistrationOverIt()
    {
        // Arrange: a subject with three registrations - the count the dialog states (6.1).
        var subject = Write("test.mm", "<map/>");
        Write("test.adp", Mindmap.Origin.MimeType + "\n");
        Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");
        Write("test.second.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(subject), TestContext.Current.CancellationToken);

        // Assert: nothing points at nothing.
        Assert.True(result.IsSuccess, result.Error);
        Assert.Empty(Directory.EnumerateFiles(_root));
    }

    [Fact]
    public async Task DeletingOneRegistration_LeavesTheSubjectAndItsPeers()
    {
        // Arrange (6.2 - the guarantee task 4's ownership provides, exercised through delete).
        Write("test.mm", "<map/>");
        var first = Write("test.first.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");
        Write("test.second.adp", Mindmap.Origin.MimeType + "\nbody: test.mm\n");

        // Act.
        var result = await _history.ExecuteAsync(new DeleteEntryCommand(first), TestContext.Current.CancellationToken);

        // Assert.
        Assert.True(result.IsSuccess, result.Error);
        var remaining = Directory.EnumerateFiles(_root).Select(IoPath.GetFileName).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "test.mm", "test.second.adp" }, remaining);
    }

    [Fact]
    public void TheConfirmation_SaysTheCountBeforeAnythingRuns()
    {
        // Arrange, act and assert: the message is where "three" is said first (6.1).
        Assert.Contains("3 diagrams registered on it",
            HierarchyContextActionProvider.DeleteConfirmationMessage("test.mm", isFolder: false, registrationCount: 3));
        Assert.Contains("1 diagram registered on it",
            HierarchyContextActionProvider.DeleteConfirmationMessage("test.mm", isFolder: false, registrationCount: 1));
        Assert.DoesNotContain("registered",
            HierarchyContextActionProvider.DeleteConfirmationMessage("plain.txt", isFolder: false));
    }
}
