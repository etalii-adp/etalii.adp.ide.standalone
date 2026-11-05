using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// <c>addTag</c>, <c>removeTag</c>, <c>renameTag</c>, <c>recolourTag</c>, <c>moveTag</c> and <c>deleteTag</c> make the
/// bytes the conformance fixtures state (spec 029 T095, FR-026, R17): <c>hierarchical-requirement-tags</c> the Markdown
/// file's, <c>hierarchical-requirement-tag-list</c> the registration's, each step run through the module's operation from
/// the surface's ids. An operation that writes both is checked against both.
/// </summary>
public sealed class TagOperationsTests
{
    private const string Tags = "hierarchical-requirement-tags";
    private const string TagList = "hierarchical-requirement-tag-list";

    /// <summary>The registration a step starts from: none, the fixture's input, or the input after its first recolour, which the steps from 3 to 12 start from.</summary>
    public enum Start
    {
        None,
        Input,
        Recoloured,
    }

    /// <summary>
    /// Every step of the two fixtures that a tag operation makes: the registration it starts from, the gesture, its target
    /// and params, then the step of <c>-tag-list</c> whose registration it writes and the step of <c>-tags</c> whose body
    /// it writes, -1 for a file it does not write.
    /// </summary>
    public static TheoryData<Start, string, string, string, int, int> Steps() => new()
    {
        { Start.None, TagOperations.AddTag, "ITEM-0002", "name=Security", -1, 0 },
        { Start.None, TagOperations.AddTag, "REQ-0002", "name=security", -1, 2 },
        { Start.None, TagOperations.AddTag, "REQ-0001", "name=ux", -1, 4 },
        { Start.None, TagOperations.AddTag, "REQ-0005", "name=security", -1, 6 },
        { Start.Input, TagOperations.AddTag, "REQ-0004", "name=user experience", 14, 8 },
        { Start.Input, TagOperations.RemoveTag, "REQ-0004", "name=ux", -1, 10 },
        { Start.Input, TagOperations.RenameTag, "tag/security", "name=infosec", 16, 12 },
        { Start.Input, TagOperations.DeleteTag, "tag/ux", "", 18, 14 },
        { Start.Input, TagOperations.RecolourTag, "tag/ux", "colour=blue", 0, -1 },
        { Start.Recoloured, TagOperations.RecolourTag, "tag/privacy", "colour=red", 3, -1 },
        { Start.Recoloured, TagOperations.MoveTag, "tag/privacy", "", 5, -1 },
        { Start.Recoloured, TagOperations.AddTag, "REQ-0004", "name=user experience", 7, 8 },
        { Start.Recoloured, TagOperations.RenameTag, "tag/security", "name=infosec", 9, 12 },
        { Start.Recoloured, TagOperations.DeleteTag, "tag/ux", "", 11, 14 },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task AGesture_WritesTheFixturesBytes(Start start, string operation, string targetId, string parameters, int registrationStep, int bodyStep)
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Tags, "requirements.md"), TestContext.Current.CancellationToken);
        var registration = start switch
        {
            Start.None => null,
            Start.Input => await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, TagList, "requirements.adp"), TestContext.Current.CancellationToken),
            _ => Encoding.UTF8.GetBytes(await ExpectAsync(TagList, 2)),
        };

        // Act.
        var edit = TagOperations.Apply(body, "requirements.md", registration, "requirements.adp", operation, targetId, Params(parameters));

        // Assert.
        Assert.Equal("", edit.Refusal);
        Assert.Equal(bodyStep < 0 ? null : await ExpectAsync(Tags, bodyStep), edit.Body is null ? null : Encoding.UTF8.GetString(edit.Body));
        Assert.Equal(registrationStep < 0 ? null : await ExpectAsync(TagList, registrationStep), edit.RegistrationAfter is null ? null : Encoding.UTF8.GetString(edit.RegistrationAfter));
        if (edit.RegistrationAfter is not null) Assert.Equal(registration, edit.RegistrationBefore);
    }

    [Fact]
    public async Task AFirstChangeOfTheList_WithoutARegistration_StartsFromTheOneItCreates()
    {
        // Arrange.
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Tags, "requirements.md"), TestContext.Current.CancellationToken);
        var created = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, TagList, "requirements.adp"), TestContext.Current.CancellationToken);

        // Act.
        var edit = TagOperations.Apply(body, "requirements.md", null, "requirements.adp", TagOperations.AddTag, "REQ-0004", Params("name=user experience"));

        // Assert: the step starts from the registration createOnFirstPlacement writes, and writes the whole list.
        Assert.Equal(created, edit.RegistrationBefore);
        Assert.Equal(await ExpectAsync(TagList, 14), Encoding.UTF8.GetString(Assert.IsType<byte[]>(edit.RegistrationAfter)));
    }

    [Fact]
    public async Task WhatCannotBeWritten_IsRefused_AndAGoneTarget_IsAChangeMadeAgainstAnOlderFile()
    {
        var body = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, Tags, "requirements.md"), TestContext.Current.CancellationToken);

        Assert.Equal("A tag needs a name.", Refusal(TagOperations.AddTag, "REQ-0001", "name=  "));
        Assert.Equal("'a#b' cannot be a tag: a tag's name is one word without #.", Refusal(TagOperations.AddTag, "REQ-0001", "name=a#b"));
        Assert.Equal("'mauve' is not a colour.", Refusal(TagOperations.RecolourTag, "tag/ux", "colour=mauve"));
        Assert.Equal("There is already a tag named UX.", Refusal(TagOperations.RenameTag, "tag/security", "name=UX"));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal(TagOperations.AddTag, "REQ-0999", "name=ux"));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal(TagOperations.DeleteTag, "tag/gone", ""));
        Assert.Equal(HierarchicalRequirementGestures.Drifted, Refusal(TagOperations.MoveTag, "tag/ux", "after=tag/gone"));

        string Refusal(string operation, string targetId, string parameters) =>
            TagOperations.Apply(body, "requirements.md", null, "requirements.adp", operation, targetId, Params(parameters)).Refusal;
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
/// The tag gestures of the page reach the files through the session and the project's history (spec 029 T095, FR-028,
/// R17): a rename or a delete writes the Markdown file and the registration as one step, which one undo takes back.
/// </summary>
public sealed class TagGesturesTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Theory]
    [InlineData(TagOperations.RenameTag, "name=infosec", "#infosec", "  infosec: red")]
    [InlineData(TagOperations.DeleteTag, "", "category:functional state:accepted", "  legal: purple")]
    public async Task ARenameOrADelete_WritesBothFiles_AsOneStep_ThatOneUndoTakesBack(string operation, string parameters, string bodyHas, string registrationHas)
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act.
        await RunAsync(page, operation, "tag/security", parameters);

        // Assert: both files changed, security is gone from both, and one undo gives both back.
        var text = await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken);
        var kept = await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken);
        Assert.Contains("REQ-0006 A session ends after 30 minutes without activity. " + bodyHas, text, StringComparison.Ordinal);
        Assert.DoesNotContain("#security", text, StringComparison.Ordinal);
        Assert.DoesNotContain("  security: red", kept, StringComparison.Ordinal);
        Assert.Contains(registrationHas, kept, StringComparison.Ordinal);
        Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        Assert.Equal(body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
        Assert.False(page.History.CanUndo);
    }

    [Fact]
    public async Task AColourOrAPlace_WritesOnlyTheRegistration_AsAStepOfTheHistory()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var body = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);
        var registration = await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken);

        // Act.
        await RunAsync(page, TagOperations.RecolourTag, "tag/search", "colour=green");
        await RunAsync(page, TagOperations.MoveTag, "tag/privacy", "after=tag/customer");

        // Assert: the Markdown file is as it was (User Story 7 scenario 3), and two undos give the registration back.
        Assert.Equal(body, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
        Assert.Contains("tags:\r\n  customer: blue\r\n  privacy: pink\r\n  search: green\r\n", await File.ReadAllTextAsync(_folder.Registration, TestContext.Current.CancellationToken), StringComparison.Ordinal);
        for (var step = 0; step < 2; step++)
        {
            Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }
        Assert.Equal(registration, await File.ReadAllBytesAsync(_folder.Registration, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ATagThePageDoesNotShow_IsRefusedAtOnce()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);

        // Act.
        (_, string refusal) = page.Gesture(TagOperations.DeleteTag, "tag/gone");

        // Assert.
        Assert.Equal(HierarchicalRequirementGestures.Drifted, refusal);
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
