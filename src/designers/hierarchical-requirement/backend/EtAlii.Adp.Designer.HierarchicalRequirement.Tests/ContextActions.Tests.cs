using EtAlii.Adp.Context;
using EtAlii.Adp.History;
using EtAlii.Adp.TestSupport.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// The keys that add an entry directly below the selected one, with no dialog (spec 029 T074, FR-012, US4 scenario 3):
/// offered for an entry of a requirement document with their shortcuts, found by those shortcuts through the host's own
/// resolver, each one step of the project's history; and nothing offered for another tool's Markdown file.
/// </summary>
public sealed class ContextActionsTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder("requirement-keys");

    private string Seed()
    {
        var body = IoPath.Combine(_projectFolder, "web-shop.md");
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"), body);
        return body;
    }

    private ContextTarget Entry(string body, string id) => ContextTarget.ForElement(body, id, ProjectRoot.From(_projectFolder));

    private IContextActionResolver Resolver => host.Services.GetRequiredService<IContextActionResolver>();

    [Fact]
    public async Task AnItem_OffersAnItemBelowAndARequirement_AndARequirement_ARequirementBelow_EachWithItsShortcut()
    {
        // Arrange.
        var body = Seed();

        // Act.
        var item = (await Resolver.DiscoverAsync(Entry(body, "ITEM-0003"), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).ToList();
        var requirement = (await Resolver.DiscoverAsync(Entry(body, "REQ-0005"), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).ToList();

        // Assert: Ctrl+Enter below, Ctrl+Shift+Enter a requirement in the item.
        Assert.Contains(item, action => action is { Label: "New item below", Available: true } && action.Shortcut == new ContextShortcutDefinition("Enter", Ctrl: true));
        Assert.Contains(item, action => action is { Label: "New requirement", Available: true } && action.Shortcut == new ContextShortcutDefinition("Enter", Ctrl: true, Shift: true));
        var below = Assert.Single(requirement, action => action.Id == HierarchicalRequirementContextActionProvider.AddBelowActionId);
        Assert.Equal(("New requirement below", true, new ContextShortcutDefinition("Enter", Ctrl: true)), (below.Label, below.Available, below.Shortcut));
    }

    [Fact]
    public async Task EachShortcut_AddsWhereTheSpecificationSays_AsOneStepOfTheHistory()
    {
        // Arrange.
        var body = Seed();
        var before = await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken);
        var history = host.Services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_projectFolder));

        // Act: an item below ITEM-0003, then a requirement in ITEM-0006, then one below REQ-0009.
        await RunAsync(body, "ITEM-0003", new ContextShortcutDefinition("Enter", Ctrl: true));
        await RunAsync(body, "ITEM-0006", new ContextShortcutDefinition("Enter", Ctrl: true, Shift: true));
        await RunAsync(body, "REQ-0009", new ContextShortcutDefinition("Enter", Ctrl: true));

        // Assert: ITEM-0008 after ITEM-0003 in ITEM-0002; REQ-0014 after REQ-0010, the last requirement of ITEM-0006 (the
        // placeholder took REQ-0013); REQ-0015 directly below REQ-0009.
        var entries = HierarchicalRequirementBody.Read(await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken), "web-shop.md").Document.Entries;
        Assert.Equal(["REQ-0003", "REQ-0004", "ITEM-0003", "ITEM-0008"], Children(entries, "ITEM-0002"));
        Assert.Equal(["REQ-0009", "REQ-0015", "REQ-0010", "REQ-0014"], Children(entries, "ITEM-0006"));
        Assert.Equal(("New item", "New requirement"), (entries.Single(entry => entry.Id == "ITEM-0008").Title, entries.Single(entry => entry.Id == "REQ-0015").Title));

        // And each was one step: three undos give the file back.
        for (var step = 0; step < 3; step++)
        {
            Assert.True((await history.UndoAsync(TestContext.Current.CancellationToken)).IsSuccess);
        }
        Assert.Equal(before, await File.ReadAllBytesAsync(body, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ARequirementInNoItem_IsOfferedNothingBelow_WithTheSpecificationsReason_AndAnotherToolsFile_NothingAtAll()
    {
        // Arrange.
        var alone = IoPath.Combine(_projectFolder, "alone.md");
        await File.WriteAllTextAsync(alone, "<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\r\n# Alone\r\n\r\n## Requirements\r\n\r\n- REQ-0001 Stands in no item.\r\n", TestContext.Current.CancellationToken);
        var notes = IoPath.Combine(_projectFolder, "notes.md");
        await File.WriteAllTextAsync(notes, "# Notes\r\n\r\n- REQ-0001 A bullet that is no requirement.\r\n", TestContext.Current.CancellationToken);

        // Act.
        var below = (await Resolver.DiscoverAsync(Entry(alone, "REQ-0001"), TestContext.Current.CancellationToken)).SelectMany(group => group.Actions).Single(action => action.Id == HierarchicalRequirementContextActionProvider.AddBelowActionId);
        var byShortcut = await Resolver.ResolveByShortcutAsync(Entry(alone, "REQ-0001"), new ContextShortcutDefinition("Enter", Ctrl: true), TestContext.Current.CancellationToken);
        var other = await Resolver.DiscoverAsync(Entry(notes, "REQ-0001"), TestContext.Current.CancellationToken);

        // Assert.
        Assert.Equal((false, StructureOperations.NoItem), (below.Available, below.UnavailableReason));
        Assert.Null(byShortcut);
        Assert.DoesNotContain(other.SelectMany(group => group.Actions), action => action.Id.StartsWith("hierarchical-requirement.", StringComparison.Ordinal));
    }

    private async Task RunAsync(string body, string id, ContextShortcutDefinition shortcut)
    {
        var target = Entry(body, id);
        var owner = await Resolver.ResolveByShortcutAsync(target, shortcut, TestContext.Current.CancellationToken);
        Assert.NotNull(owner);
        Assert.IsType<ContextExecutionCompleted>(await owner.Provider.ExecuteAsync(target, owner.Action.Id, TestContext.Current.CancellationToken));
    }

    private static List<string> Children(IReadOnlyList<RequirementEntry> entries, string parent) =>
        [.. entries.Where(entry => entry.ParentId == parent).Select(entry => entry.Id)];
}
