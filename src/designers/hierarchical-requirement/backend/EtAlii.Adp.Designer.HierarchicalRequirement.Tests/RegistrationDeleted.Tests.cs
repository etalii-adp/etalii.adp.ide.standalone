using Xunit;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// Deleting a requirement document's registration loses nothing an agent reads (spec 029 T096, FR-004, US7 scenario 2,
/// SC-008): the document stays, an open page goes on with the lists' default colours and order, reopening creates no
/// registration until a setting changes, and with one created again every tag, category, state, reference and assessment
/// is there. Only colours, order, what was collapsed and stored filters are gone (028 User Story 7 scenario 5).
/// </summary>
public sealed class RegistrationDeletedTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AnOpenPage_GoesOnWithTheDefaultColoursAndOrder_AndTheDocumentStays()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var before = page.Session.Model();
        var sent = page.Models.Count;

        // Act.
        File.Delete(_folder.Registration);

        // Assert: the page is sent the document without a registration, and the body is untouched.
        var after = await page.ModelAsync(sent, model => model.Filters.Count == 0);
        Assert.NotNull(after);
        var tags = after.OptionLists.Single(list => list.Label == "tags").Options;
        Assert.Equal(Carried(before), [.. tags.Select(tag => tag.Name.ToLowerInvariant()).Order(StringComparer.Ordinal)]);
        Assert.All(tags, tag => Assert.Equal(("default", true), (tag.Colour, tag.Implicit)));
        Assert.Equal(Initial("categories"), Colours(after, "category"));
        Assert.Equal(Initial("states"), Colours(after, "state"));
        Assert.DoesNotContain(after.Nodes, node => node.Collapsed);
        Assert.Equal(Kept(before), Kept(after));
        Assert.Equal(body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(_folder.Registration));
    }

    [Fact]
    public async Task Reopening_CreatesNoRegistration_UntilASettingChanges()
    {
        // Arrange.
        File.Delete(_folder.Registration);

        // Act: opened, and an entry's text changed, which is no setting.
        await using var page = RequirementPage.Open(_folder.Body);
        await RunAsync(page, TextOperations.SetTitle, "ITEM-0001", "title", "Shop front");
        var createdByAnEdit = File.Exists(_folder.Registration);
        await RunAsync(page, TagOperations.RecolourTag, "tag/core", "colour", "orange");

        // Assert.
        Assert.False(createdByAnEdit);
        Assert.True(File.Exists(_folder.Registration));
        Assert.Contains("  core: orange\r\n", await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeletedAndCreatedAgain_TheDocumentHasEveryTagCategoryStateReferenceAndAssessment()
    {
        // Arrange.
        Model before;
        await using (var first = RequirementPage.Open(_folder.Body, _folder.Registration))
        {
            before = Kept(first.Session.Model());
        }

        // Act: deleted, then created again by the first setting of a page opened without it.
        File.Delete(_folder.Registration);
        await using var page = RequirementPage.Open(_folder.Body);
        await RunAsync(page, TagOperations.RecolourTag, "tag/legal", "colour", "purple");
        await using var reopened = RequirementPage.Open(_folder.Body, _folder.Registration);

        // Assert: the same document, and a list the created registration holds whole.
        Assert.True(File.Exists(_folder.Registration));
        Assert.Equal(before, Kept(reopened.Session.Model()));
        Assert.DoesNotContain(reopened.Session.Model().OptionLists.SelectMany(list => list.Options), option => option.Implicit);
    }

    /// <summary>
    /// What the Markdown file holds of the page: each node's tags, category and state, and its references; the assessments;
    /// and the names of each list's members, whose order and colours are the registration's.
    /// </summary>
    private static Model Kept(HierarchySurfaceModel model) => new(
        string.Join("\n", model.Nodes.Select(node =>
            $"{node.Id} {string.Join(" ", node.Labels.Select(values => $"{values.Label}={string.Join(",", values.Values)}"))} {string.Join(",", node.References.Select(reference => reference.TargetId))}")),
        string.Join("\n", model.Notes.Select(note => $"{note.Id} {note.Kind} {note.About} {note.Text} {note.Resolved}")),
        string.Join("\n", model.OptionLists.Select(list => $"{list.Label}: {string.Join(",", list.Options.Select(option => option.Name).Order(StringComparer.Ordinal))}")));

    /// <summary>The tags the page's entries carry, once each in any case, sorted.</summary>
    private static List<string> Carried(HierarchySurfaceModel model) =>
        [.. model.Nodes.SelectMany(node => node.Labels).Where(values => values.Label == "tags").SelectMany(values => values.Values).Select(tag => tag.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal)];

    private static List<(string, string)> Colours(HierarchySurfaceModel model, string label) =>
        [.. model.OptionLists.Single(list => list.Label == label).Options.Where(option => !option.Implicit).Select(option => (option.Name, option.Colour))];

    private static List<(string, string)> Initial(string block) =>
        [.. HierarchicalRequirementDefinition.Initial(block).Select(member => (member.Key, member.Value))];

    private static async Task RunAsync(RequirementPage page, string operation, string targetId, string name, string value)
    {
        (ShortGuid editId, string refusal) = page.Gesture(operation, targetId, (name, value));
        Assert.Equal("", refusal);
        var outcome = await page.OutcomeAsync(editId);
        Assert.True(outcome?.Written, outcome?.Error);
    }

    private sealed record Model(string Nodes, string Notes, string Lists);
}
