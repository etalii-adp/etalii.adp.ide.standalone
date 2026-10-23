using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The category and state operations make the bytes the conformance fixtures state (spec 029 T102, FR-026, FR-030,
/// FR-031): <c>hierarchical-requirement-category-state</c> the Markdown file's, its <c>list</c> the registration's, each
/// step run through the module's operation from the surface's ids. Of several <c>category:</c> words the first counts:
/// a set or a clear changes that one and keeps the others as written.
/// </summary>
public sealed class CategoryStateOperationsTests
{
    private const string CategoryState = "hierarchical-requirement-category-state";
    private const string List = "hierarchical-requirement-category-state/list";

    /// <summary>
    /// Every step of the two fixtures that an operation makes: the step of the body's fixture it starts from (-1 for the
    /// input), whether it starts from the list's registration, the gesture, its target and params, then the step of the
    /// body's fixture and of the list's whose bytes it writes, -1 for a file it does not write.
    /// </summary>
    public static TheoryData<int, bool, string, string, string, int, int> Steps() => new()
    {
        { -1, false, "setCategory", "REQ-0002", "category=functional", 0, -1 },
        { -1, false, "setState", "REQ-0001", "state=proposed", 2, -1 },
        { -1, false, "setCategory", "REQ-0004", "category=Functional", 4, -1 },
        { -1, false, "setState", "REQ-0003", "state=rejected", 6, -1 },
        { -1, false, "clearCategory", "REQ-0003", "", 8, -1 },
        { 8, false, "clearState", "REQ-0003", "", 9, -1 },
        { -1, false, "clearCategory", "REQ-0004", "", 12, -1 },
        { -1, true, "renameCategory", "category/technical", "name=engineering", 14, 2 },
        { -1, true, "addCategory", "document", "name=security|colour=red", -1, 0 },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task AGesture_WritesTheFixturesBytes(int from, bool listed, string operation, string targetId, string parameters, int bodyStep, int registrationStep)
    {
        // Arrange.
        var body = from < 0
            ? await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, CategoryState, "requirements.md"), TestContext.Current.CancellationToken)
            : Encoding.UTF8.GetBytes(await ExpectAsync(CategoryState, from));
        var registration = listed ? await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, List, "requirements.adp"), TestContext.Current.CancellationToken) : null;

        // Act.
        var edit = CategoryStateOperations.Apply(body, "requirements.md", registration, "requirements.adp", operation, targetId, Params(parameters));

        // Assert.
        Assert.Equal("", edit.Refusal);
        Assert.Equal(bodyStep < 0 ? null : await ExpectAsync(CategoryState, bodyStep), edit.Body is null ? null : Encoding.UTF8.GetString(edit.Body));
        Assert.Equal(registrationStep < 0 ? null : await ExpectAsync(List, registrationStep), edit.RegistrationAfter is null ? null : Encoding.UTF8.GetString(edit.RegistrationAfter));
    }

    [Fact]
    public async Task AFirstChangeOfAList_WithoutARegistration_StartsFromTheOneItCreates()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, CategoryState, "requirements.md"), TestContext.Current.CancellationToken);
        var created = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, List, "requirements.adp"), TestContext.Current.CancellationToken);

        // Act.
        var edit = CategoryStateOperations.Apply(body, "requirements.md", null, "requirements.adp", "addCategory", "document", Params("name=security|colour=red"));

        // Assert: the step starts from the registration createOnFirstPlacement writes, and writes the whole list, legal included.
        Assert.Equal(created, edit.RegistrationBefore);
        Assert.Equal(await ExpectAsync(List, 0), Encoding.UTF8.GetString(Assert.IsType<byte[]>(edit.RegistrationAfter)));
        Assert.Null(edit.Body);
    }

    [Fact]
    public async Task ANameOfTwoWords_IsWrittenWithADash_OnTheCarriersAndInTheList()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, CategoryState, "requirements.md"), TestContext.Current.CancellationToken);

        // Act.
        var edit = CategoryStateOperations.Apply(body, "requirements.md", null, "requirements.adp", "renameCategory", "category/technical", Params("name= built in "));

        // Assert: p.name.trim().replace(' ', '-'), as the operation writes it.
        Assert.Contains("category:built-in", Encoding.UTF8.GetString(Assert.IsType<byte[]>(edit.Body)), StringComparison.Ordinal);
        Assert.Contains("built-in:", Encoding.UTF8.GetString(Assert.IsType<byte[]>(edit.RegistrationAfter)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecolourOrAMove_WritesOnlyTheList()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, CategoryState, "requirements.md"), TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, List, "requirements.adp"), TestContext.Current.CancellationToken);

        // Act: each from the registration without the list, which its first change writes whole.
        var recoloured = CategoryStateOperations.Apply(body, "requirements.md", registration, "requirements.adp", "recolourState", "state/proposed", Params("colour=orange"));
        var first = CategoryStateOperations.Apply(body, "requirements.md", registration, "requirements.adp", "moveCategory", "category/technical", Params("after="));
        var after = CategoryStateOperations.Apply(body, "requirements.md", registration, "requirements.adp", "moveCategory", "category/functional", Params("after=category/technical"));

        // Assert.
        Assert.Contains("  proposed: orange\r\n", Encoding.UTF8.GetString(Assert.IsType<byte[]>(recoloured.RegistrationAfter)), StringComparison.Ordinal);
        Assert.Equal(["technical", "functional", "non-functional", "legal"], Keys(first, "categories"));
        Assert.Equal(["non-functional", "technical", "functional", "legal"], Keys(after, "categories"));
        Assert.All([recoloured, first, after], edit => Assert.Null(edit.Body));

        static string[] Keys(ListEdit edit, string block) =>
        [
            .. Encoding.UTF8.GetString(Assert.IsType<byte[]>(edit.RegistrationAfter)).Split(block + ":\r\n")[1].Split("\r\n")
                .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal)).Select(line => line.Trim().Split(':')[0]),
        ];
    }

    [Fact]
    public async Task WhatCannotBeWritten_IsRefused_AndAGoneTarget_IsAChangeMadeAgainstAnOlderFile()
    {
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, CategoryState, "requirements.md"), TestContext.Current.CancellationToken);

        Assert.Equal("A category needs a name.", Refusal("addCategory", "document", "name= "));
        Assert.Equal("'mauve' is not a colour.", Refusal("recolourState", "state/proposed", "colour=mauve"));
        Assert.Equal("There is already a category named LEGAL.", Refusal("renameCategory", "category/technical", "name=LEGAL"));
        Assert.Equal("There is already a state named done.", Refusal("addState", "document", "name=done"));
        Assert.Equal("'urgent' is not a state of this document.", Refusal("setState", "REQ-0001", "state=urgent"));
        Assert.Equal("'a\tb' cannot be a category: a category's name is one word.", Refusal("addCategory", "document", "name=a\tb"));
        Assert.Equal("Pick a state.", Refusal("setState", "REQ-0001", "state= "));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal("setCategory", "ITEM-0001", "category=legal"));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal("deleteState", "state/gone", ""));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal("moveCategory", "category/legal", "after=category/gone"));

        string Refusal(string operation, string targetId, string parameters) =>
            CategoryStateOperations.Apply(body, "requirements.md", null, "requirements.adp", operation, targetId, Params(parameters)).Refusal;
    }

    private static async Task<string> ExpectAsync(string fixture, int step)
    {
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, fixture, "fixture.json"), TestContext.Current.CancellationToken));
        return json.RootElement.GetProperty("steps")[step].GetProperty("expect").GetString()!;
    }

    private static Dictionary<string, string> Params(string parameters) =>
        parameters.Length == 0
            ? []
            : parameters.Split('|').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
}

/// <summary>
/// The category and state gestures of the page reach the files through the session and the project's history (spec 029
/// T102, R17): a rename or a delete writes the Markdown file and the registration as one step, which one undo takes back,
/// and a pick writes the Markdown file alone.
/// </summary>
public sealed class CategoryStateGesturesTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData("renameCategory", "name=behaviour", "category:behaviour state:accepted", "  behaviour: blue")]
    [InlineData("deleteCategory", "", "state:accepted", "  non-functional: purple")]
    public async Task ARenameOrADelete_WritesBothFiles_AsOneStep_ThatOneUndoTakesBack(string operation, string parameters, string bodyHas, string registrationHas)
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act.
        await RunAsync(page, operation, "category/functional", parameters);

        // Assert: both files changed, functional is gone from both - the file's own example of the notation aside - and one undo gives both back.
        var text = await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken);
        var kept = await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken);
        Assert.Contains("REQ-0006 A session ends after 30 minutes without activity. #security " + bodyHas, text, StringComparison.Ordinal);
        Assert.DoesNotContain("REQ-0003 A customer pays by card, iDEAL or PayPal. category:functional", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  functional: blue", kept, StringComparison.Ordinal);
        Assert.Contains(registrationHas, kept, StringComparison.Ordinal);
        Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
        Assert.False(page.History.CanUndo);
    }

    [Fact]
    public async Task APick_WritesOnlyTheMarkdownFile_WithTheListsSpelling()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act.
        await RunAsync(page, "setState", "REQ-0006", "state=DONE");

        // Assert.
        Assert.Contains("REQ-0006 A session ends after 30 minutes without activity. #security category:functional state:done", await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AMemberThePageDoesNotShow_OrAPickOfNothing_IsRefusedAtOnce()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);

        // Act.
        (_, string refusal) = page.Gesture("deleteState", "state/gone");
        (_, string unpicked) = page.Gesture("setState", "REQ-0001", ("state", " "));

        // Assert.
        Assert.Equal(HierarchicalRequirementGestures.Drifted, refusal);
        Assert.Equal("Pick a state.", unpicked);
    }

    private static async Task RunAsync(RequirementPage page, string operation, string targetId, string parameters)
    {
        var pairs = parameters.Length == 0 ? [] : parameters.Split('|').Select(pair => pair.Split('=', 2)).Select(pair => (pair[0], pair[1])).ToArray();
        (ShortGuid editId, string refusal) = page.Gesture(operation, targetId, pairs);
        Assert.Equal("", refusal);
        var outcome = await page.OutcomeAsync(editId);
        Assert.True(outcome?.Written, outcome?.Error);
    }
}
