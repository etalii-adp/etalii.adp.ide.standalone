using System.Text;
using System.Text.Json;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The adds, the moves, the delete and giving a duplicate a number make the bytes the conformance fixtures state (spec
/// 029 T073, FR-026, R17): each step is the fixture's own, run through the module's operation from the surface's ids,
/// against the bytes the step before it left.
/// </summary>
public sealed class StructureOperationsTests
{
    /// <summary>Every step of the corpus that shapes the hierarchy: the fixture, the step, and the gesture that makes it.</summary>
    public static TheoryData<string, int, string, string, string> Steps() => new()
    {
        { "hierarchical-requirement-structure", 0, StructureOperations.AddRequirementBelow, "REQ-0004", "" },
        { "hierarchical-requirement-structure", 1, StructureOperations.AddItem, "ITEM-0001", "" },
        { "hierarchical-requirement-structure", 2, StructureOperations.MoveItem, "ITEM-0003", "item=ITEM-0004" },
        { "hierarchical-requirement-structure", 3, StructureOperations.DeleteEntry, "ITEM-0002", "" },
        { "hierarchical-requirement-structure", 4, StructureOperations.AddRequirement, "ITEM-0001", "" },
        { "hierarchical-requirement-structure/no-section", 0, StructureOperations.AddTopItem, "", "" },
        { "hierarchical-requirement-new", 1, StructureOperations.AddTopItem, "", "" },
        { "hierarchical-requirement-edits/no-mark", 2, StructureOperations.AddTopItem, "", "" },
        { "hierarchical-requirement-edits", 3, StructureOperations.RenumberDuplicate, "line/14", "" },
    };

    [Theory]
    [MemberData(nameof(Steps))]
    public async Task AGesture_WritesTheFixturesBytes(string fixture, int step, string operation, string targetId, string parameters)
    {
        // Arrange: the bytes the step starts from, the input's or those the step before it states.
        var folder = IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, fixture);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(IoPath.Combine(folder, "fixture.json"), TestContext.Current.CancellationToken));
        var inputName = json.RootElement.GetProperty("input").GetString()!;
        var steps = json.RootElement.GetProperty("steps");
        var input = step == 0 ? await File.ReadAllBytesAsync(IoPath.Combine(folder, inputName), TestContext.Current.CancellationToken) : Encoding.UTF8.GetBytes(Expected(folder, steps[step - 1]));

        // Act.
        (byte[]? bytes, string refusal) = StructureOperations.Apply(input, inputName, operation, targetId, Params(parameters));

        // Assert.
        Assert.Equal("", refusal);
        Assert.Equal(Expected(folder, steps[step]), Encoding.UTF8.GetString(Assert.IsType<byte[]>(bytes)));
    }

    [Fact]
    public void AnEntryHoldingPlaceholders_IsDeletedAndMoved_AfterTheyAreNumbered()
    {
        // Arrange: ITEM-NEW on line 10 holds a REQ-NEW; both are numbered by the edit that deletes or moves them.
        var input = Numbering();

        // Act.
        (byte[]? deleted, string deleteRefusal) = StructureOperations.Apply(input, "web-shop.md", StructureOperations.DeleteEntry, "line/10", Params(""));
        (byte[]? moved, string moveRefusal) = StructureOperations.Apply(input, "web-shop.md", StructureOperations.MoveItem, "line/10", Params("after=ITEM-0002"));

        // Assert: the numbers are given and kept by the marks, the item goes with what it holds.
        Assert.Equal("", deleteRefusal);
        Assert.Equal(
            "<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\n<!-- last: ITEM-0003 REQ-0005 -->\n# Web shop\n\n## Requirements\n\n" +
            "- ITEM-0001 Backend #core\n  - REQ-0001 A session ends after 30 minutes without activity. #security\n  - REQ-0004 Tokens are refreshed without the user noticing.\n" +
            "- ITEM-0002 Data\n  - REQ-0002 Personal data is kept no longer than it is needed.\n",
            Encoding.UTF8.GetString(Assert.IsType<byte[]>(deleted)).ReplaceLineEndings("\n"));
        Assert.Equal("", moveRefusal);
        Assert.Equal(
            "<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\n<!-- last: ITEM-0003 REQ-0005 -->\n# Web shop\n\n## Requirements\n\n" +
            "- ITEM-0001 Backend #core\n  - REQ-0001 A session ends after 30 minutes without activity. #security\n  - REQ-0004 Tokens are refreshed without the user noticing.\n" +
            "- ITEM-0002 Data\n  - REQ-0002 Personal data is kept no longer than it is needed.\n" +
            "- ITEM-0003 Payments\n  - REQ-0005 A customer pays by card or by bank transfer. category:functional\n",
            Encoding.UTF8.GetString(Assert.IsType<byte[]>(moved)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AMoveWithinItsParent_GoesAfterTheSiblingNamed_OrToTheFrontOfItsKind()
    {
        var input = Catalogue();

        (byte[]? first, _) = StructureOperations.Apply(input, "catalogue.md", StructureOperations.MoveRequirement, "REQ-0006", Params("item=ITEM-0003"));
        (byte[]? last, _) = StructureOperations.Apply(input, "catalogue.md", StructureOperations.MoveRequirement, "REQ-0004", Params("item=ITEM-0003;after=REQ-0006"));
        (byte[]? same, _) = StructureOperations.Apply(input, "catalogue.md", StructureOperations.MoveItem, "ITEM-0001", Params(""));

        Assert.Equal(["REQ-0006", "REQ-0004"], Children(first!, "ITEM-0003"));
        Assert.Equal(["REQ-0006", "REQ-0004"], Children(last!, "ITEM-0003"));
        Assert.Equal(input, same);
    }

    [Fact]
    public void ARequirementMovedIntoAnotherItem_GoesWhereANewRequirementWould()
    {
        var input = Catalogue();

        (byte[]? afterRequirements, _) = StructureOperations.Apply(input, "catalogue.md", StructureOperations.MoveRequirement, "REQ-0007", Params("item=ITEM-0001"));
        (byte[]? intoEmpty, _) = StructureOperations.Apply(input, "catalogue.md", StructureOperations.MoveRequirement, "REQ-0001", Params("item=ITEM-0004;after=REQ-0007"));

        Assert.Equal(["REQ-0001", "REQ-0002", "REQ-0007", "ITEM-0002"], Children(afterRequirements!, "ITEM-0001"));
        Assert.Equal(["REQ-0007", "REQ-0001"], Children(intoEmpty!, "ITEM-0004"));
    }

    [Fact]
    public void WhatTheSpecificationRollsBackOrMakesUnavailable_IsRefused_AndNothingIsWritten()
    {
        var catalogue = Catalogue();
        var topLevel = Encoding.UTF8.GetBytes("<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\r\n# Alone\r\n\r\n## Requirements\r\n\r\n- REQ-0001 Stands in no item.\r\n");

        Assert.Equal((null, StructureOperations.NoItem), StructureOperations.Apply(topLevel, "alone.md", StructureOperations.AddRequirementBelow, "REQ-0001", Params("")));
        Assert.Equal((null, "ITEM-0001 cannot be moved into itself or into anything it holds."), StructureOperations.Apply(catalogue, "catalogue.md", StructureOperations.MoveItem, "ITEM-0001", Params("item=ITEM-0001")));
        Assert.Equal((null, "ITEM-0001 cannot be moved into itself or into anything it holds."), StructureOperations.Apply(catalogue, "catalogue.md", StructureOperations.MoveItem, "ITEM-0001", Params("item=ITEM-0002")));
        Assert.Equal((null, "REQ-0001 is a requirement, which holds nothing."), StructureOperations.Apply(catalogue, "catalogue.md", StructureOperations.MoveItem, "ITEM-0003", Params("item=REQ-0001")));
        Assert.Equal((null, "REQ-0001 is the only entry with its number, which it keeps."), StructureOperations.Apply(catalogue, "catalogue.md", StructureOperations.RenumberDuplicate, "REQ-0001", Params("")));
        Assert.Equal((null, HierarchicalRequirementGestures.Drifted), StructureOperations.Apply(catalogue, "catalogue.md", StructureOperations.DeleteEntry, "ITEM-0099", Params("")));
        Assert.Equal((null, HierarchicalRequirementGestures.Drifted), StructureOperations.Apply(catalogue, "catalogue.md", StructureOperations.MoveItem, "ITEM-0003", Params("after=ITEM-0099")));
    }

    private static string Expected(string folder, JsonElement step) =>
        step.TryGetProperty("expect", out var expect) ? expect.GetString()! : File.ReadAllText(IoPath.Combine(folder, step.GetProperty("expectFile").GetString()!));

    private static Dictionary<string, string> Params(string written) =>
        written.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(pair => pair.Split('=')).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

    private static byte[] Catalogue() => File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, "hierarchical-requirement-structure", "catalogue.md"));

    private static byte[] Numbering() => File.ReadAllBytes(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, "hierarchical-requirement-edits", "numbering", "web-shop.md"));

    /// <summary>The numbers of the entries directly in <paramref name="parent"/>, in their order.</summary>
    private static List<string> Children(byte[] body, string parent) =>
        [.. HierarchicalRequirementBody.Read(body, "catalogue.md").Document.Entries.Where(entry => entry.ParentId == parent).Select(entry => entry.Id)];
}
