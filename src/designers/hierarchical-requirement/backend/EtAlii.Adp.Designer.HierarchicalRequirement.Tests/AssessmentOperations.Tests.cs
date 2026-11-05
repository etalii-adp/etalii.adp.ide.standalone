using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// <c>addAssessment</c>, <c>changeAssessment</c>, <c>resolveAssessment</c> and <c>removeAssessment</c> make the bytes the
/// conformance fixtures state (spec 029 T083, FR-026, R17): each step is the fixture's own, run through the module's
/// operation from the surface's ids, and each starts from the input, since every edit there is followed by its undo.
/// </summary>
public sealed class AssessmentOperationsTests
{
    /// <summary>Every step of the corpus that changes an assessment: the fixture, the step, the gesture and its params.</summary>
    public static TheoryData<string, int, string, string, string> Steps() => new()
    {
        { "hierarchical-requirement-assessments", 0, AssessmentOperations.AddAssessment, "REQ-0003", "text=Does a refreshed token count as activity?" },
        { "hierarchical-requirement-assessments", 2, AssessmentOperations.ChangeAssessment, "assessment/0", "kind=Remark|text=Is 30 minutes right on a phone?|detail=" },
        { "hierarchical-requirement-assessments", 4, AssessmentOperations.ResolveAssessment, "assessment/0", "" },
        { "hierarchical-requirement-assessments", 6, AssessmentOperations.ResolveAssessment, "assessment/1", "" },
        { "hierarchical-requirement-assessments", 8, AssessmentOperations.ResolveAssessment, "assessment/2", "" },
        { "hierarchical-requirement-assessments", 10, AssessmentOperations.RemoveAssessment, "assessment/1", "" },
        { "hierarchical-requirement-assessments/first-assessment", 0, AssessmentOperations.AddAssessment, "REQ-0001", "kind=question|text=Is 30 minutes right for the mobile app?" },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task AGesture_WritesTheFixturesBytes(string fixture, int step, string operation, string targetId, string parameters)
    {
        // Arrange.
        var folder = IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, fixture);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(folder, "fixture.json"), TestContext.Current.CancellationToken));
        var inputName = json.RootElement.GetProperty("input").GetString()!;
        var input = await File.ReadAllBytesAsync(IoPath.Combine(folder, inputName), TestContext.Current.CancellationToken);

        // Act.
        (byte[]? bytes, string refusal) = AssessmentOperations.Apply(input, inputName, operation, targetId, Params(parameters));

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(json.RootElement.GetProperty("steps")[step].GetProperty("expect").GetString(), Encoding.UTF8.GetString(Assert.IsType<byte[]>(bytes)));
    }

    [Fact]
    public void AnAssessmentOfAPlaceholder_NamesTheNumberTheSameEditGivesIt()
    {
        // Arrange: the first REQ-NEW of the numbering fixture, which any edit gives a number.
        var input = File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, "hierarchical-requirement-edits", "numbering", "web-shop.md"));
        var placeholder = HierarchicalRequirementSurfaceMapper.NodeIds(HierarchicalRequirementBody.Read(input, "web-shop.md").Document.Entries).First(pair => pair.Key.Placeholder && pair.Key is Requirement).Value;

        // Act.
        (byte[]? bytes, string refusal) = AssessmentOperations.Apply(input, "web-shop.md", AssessmentOperations.AddAssessment, placeholder, Params("kind=Warning|text=Is this one needed?"));

        // Assert: the assessment names the number the placeholder now has in the file.
        Assert.Equal("", refusal);
        var read = HierarchicalRequirementBody.Read(Assert.IsType<byte[]>(bytes), "web-shop.md").Document;
        var added = read.Assessments[^1];
        Assert.Equal((AssessmentKind.Warning, "Is this one needed?", false), (added.Kind, added.Text, added.Resolved));
        Assert.Contains(read.Entries, entry => entry.Id == added.About && entry is Requirement && !entry.Placeholder);
    }

    [Fact]
    public void WhatCannotBeWritten_IsRefused_AndAGoneTarget_IsAChangeMadeAgainstAnOlderFile()
    {
        var input = File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, "hierarchical-requirement-assessments", "web-shop.md"));

        Assert.Equal((null, "An assessment needs a text."), AssessmentOperations.Apply(input, "web-shop.md", AssessmentOperations.AddAssessment, "REQ-0001", Params("text= ")));
        Assert.Equal((null, "The text of an assessment is one line; its detail holds the lines below it."), AssessmentOperations.Apply(input, "web-shop.md", AssessmentOperations.ChangeAssessment, "assessment/0", Params("kind=Question|text=One\nTwo")));
        Assert.Equal((null, "'Doubt' is not a kind of assessment: a question, remark, warning or idea."), AssessmentOperations.Apply(input, "web-shop.md", AssessmentOperations.AddAssessment, "REQ-0001", Params("kind=Doubt|text=Hm")));
        Assert.Equal((null, HierarchicalRequirementGestures.Drifted), AssessmentOperations.Apply(input, "web-shop.md", AssessmentOperations.ResolveAssessment, "assessment/9", Params("")));
        Assert.Equal((null, HierarchicalRequirementGestures.Drifted), AssessmentOperations.Apply(input, "web-shop.md", AssessmentOperations.AddAssessment, "REQ-0999", Params("text=Gone")));
    }

    private static Dictionary<string, string> Params(string parameters) =>
        parameters.Length == 0
            ? []
            : parameters.Split('|').Select(pair => pair.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);
}

/// <summary>
/// The four assessment gestures of the page reach the file through the session and the project's history (spec 029 T087,
/// FR-015, FR-016): each one step of Undo, an assessment named by its id in the model the page was sent, and an id the
/// page no longer shows refused at once.
/// </summary>
public sealed class AssessmentGesturesTests : IDisposable
{
    private readonly WebShopFolder _folder = new();

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task EachGesture_IsWritten_AsOneStepOfTheHistory()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);
        var before = await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken);

        // Act: a question added to REQ-0004, the example's first assessment resolved, its third changed, its second removed.
        await RunAsync(page, AssessmentOperations.AddAssessment, "REQ-0004", ("kind", "Question"), ("text", "Is a second e-mail sent?"));
        await RunAsync(page, AssessmentOperations.ResolveAssessment, "assessment/0");
        await RunAsync(page, AssessmentOperations.ChangeAssessment, "assessment/2", ("kind", "Warning"), ("text", "Two customers asked for Klarna."), ("detail", "Both in October."));
        await RunAsync(page, AssessmentOperations.RemoveAssessment, "assessment/1");

        // Assert: the file says each, and four undos give the file back.
        var text = await File.ReadAllTextAsync(_folder.Body, TestContext.Current.CancellationToken);
        Assert.Contains("- [ ] Question REQ-0004: Is a second e-mail sent?", text, StringComparison.Ordinal);
        Assert.Contains("- [x] Question REQ-0006: Is 30 minutes right for the mobile app?", text, StringComparison.Ordinal);
        Assert.Contains("- [ ] Warning REQ-0003: Two customers asked for Klarna.\r\n  Both in October.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Overlaps with the payment provider", text, StringComparison.Ordinal);
        for (var step = 0; step < 4; step++)
        {
            Assert.True((await page.History.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(_folder.Body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AnAssessmentThePageDoesNotShow_IsRefusedAtOnce()
    {
        // Arrange.
        await using var page = RequirementPage.Open(_folder.Body, _folder.Registration);

        // Act.
        (_, string refusal) = page.Gesture(AssessmentOperations.ResolveAssessment, "assessment/9");

        // Assert.
        Assert.Equal(HierarchicalRequirementGestures.Drifted, refusal);
    }

    private static async Task RunAsync(RequirementPage page, string operation, string targetId, params (string Name, string Value)[] parameters)
    {
        (ShortGuid editId, string refusal) = page.Gesture(operation, targetId, parameters);
        Assert.Equal("", refusal);
        var outcome = await page.OutcomeAsync(editId);
        Assert.True(outcome?.Written, outcome?.Error);
    }
}
