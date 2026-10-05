using Xunit;

namespace EtAlii.Adp.Specification.Disl.Tests;

/// <summary>The context menus of each bundled definition (DISL §7.3), grouped by <c>x-menu.group</c> (decision D2).</summary>
public class ContextMenuDerivationTests
{
    private static readonly DislEnv Readable = new(ReadOnly: false, Viewpoint: "trueTime");

    /// <summary>One line per entry, prefixed with its group's number and name, as the parity transcripts write menus.</summary>
    private static List<string> Lines(IReadOnlyList<DerivedMenuGroup> groups) =>
    [
        .. groups.SelectMany((group, index) => group.Entries.Select(entry =>
            $"g{index + 1} {group.Name} {entry.Id} | {entry.Label} | {entry.Icon} | {entry.Shortcut?.Text ?? "-"} | {(entry.Available ? "on" : "off")}{(entry.UnavailableReason.Length > 0 ? $" ({entry.UnavailableReason})" : "")}")),
    ];

    private static IReadOnlyList<DerivedMenuGroup> HypeCycle(DislMenuTarget target, DislEnv? env = null) =>
        ContextMenuDerivation.Derive(Derivations.HypeCycle, target, env ?? Readable, Derivations.HypeCycleIds);

    private static IReadOnlyList<DerivedMenuGroup> BehaviorModel(DislMenuTarget target) =>
        ContextMenuDerivation.Derive(Derivations.BehaviorModel, target, new DislEnv(), Derivations.BehaviorModelIds);

    [Fact]
    public void ATrend_OffersItsEdits_ThenArrangeInAGroupOfItsOwn()
    {
        var diagram = Derivations.HypeCycleDiagram();

        var groups = HypeCycle(DislMenuTarget.Element(Derivations.Element(diagram, "steam")));

        Assert.Equal(
        [
            "g1 edit ghg.rename | Rename… | mdi-pencil-outline | F2 | on",
            "g1 edit ghg.remove | Remove | mdi-delete-outline | Delete | on",
            "g2 arrange ghg.arrange | Arrange diagram | mdi-sitemap-outline | - | on",
        ], Lines(groups));
    }

    [Fact]
    public void ATrendWithADraggedBoundary_OffersEvenPhases()
    {
        var diagram = Derivations.HypeCycleDiagram();

        var groups = HypeCycle(DislMenuTarget.Element(Derivations.Element(diagram, "rail")));

        Assert.Equal("g1 edit ghg.even-phases | Even phases | mdi-arrow-split-vertical | - | on", Lines(groups)[1]);
    }

    [Fact]
    public void AnInfluence_IsRemovedByItsOwnAction()
    {
        var diagram = Derivations.HypeCycleDiagram();

        var groups = HypeCycle(DislMenuTarget.Element(Derivations.Element(diagram, "i1")));

        Assert.Equal(
        [
            "g1 edit ghg.disconnect | Remove influence | mdi-vector-polyline-remove | Delete | on",
            "g2 arrange ghg.arrange | Arrange diagram | mdi-sitemap-outline | - | on",
        ], Lines(groups));
    }

    [Fact]
    public void ReadOnly_OffersNoGroupsAtAll()
    {
        var diagram = Derivations.HypeCycleDiagram();

        Assert.Empty(HypeCycle(DislMenuTarget.Element(Derivations.Element(diagram, "steam")), Readable with { ReadOnly = true }));
        Assert.Empty(HypeCycle(DislMenuTarget.Canvas(diagram), Readable with { ReadOnly = true }));
    }

    [Fact]
    public void TheEmptyCanvas_OffersTheAdds_AndArrangeUnavailableUntilThereIsSomething()
    {
        var groups = HypeCycle(DislMenuTarget.Canvas(new DislDiagram(Derivations.HypeCycle)));

        Assert.Equal(
        [
            "g1 add ghg.add.trend | Add trend here | mdi-plus | - | on",
            "g1 add ghg.add.trigger | Add trigger here | mdi-circle-slice-8 | - | on",
            "g1 add ghg.add.note | Add note here | mdi-note-text-outline | - | on",
            "g2 arrange ghg.arrange | Arrange diagram | mdi-sitemap-outline | - | off (There is nothing to arrange until this graph has a trend.)",
        ], Lines(groups));
    }

    [Fact]
    public void ASetsWhen_KeepsTheCanvasMenuToItsViewpoint()
    {
        var groups = HypeCycle(DislMenuTarget.Canvas(Derivations.HypeCycleDiagram()), Readable with { Viewpoint = "compact" });

        Assert.Empty(groups);
    }

    [Fact]
    public void AConnection_IsDrawnAsAnInfluence()
    {
        var diagram = Derivations.HypeCycleDiagram();

        var groups = HypeCycle(DislMenuTarget.Connection(diagram, Derivations.Element(diagram, "steam"), null));

        Assert.Equal(["g1  ghg.connect.influence | Influence | mdi-ray-start-arrow | - | on"], Lines(groups));
    }

    [Fact]
    public void ABehaviorNode_OffersItsEdits_OneAddChildPerKind_AndArrange()
    {
        var diagram = Derivations.BehaviorDiagram();

        var lines = Lines(BehaviorModel(DislMenuTarget.Element(Derivations.Element(diagram, "1.1"))));

        Assert.Equal(
        [
            "g1 edit abm.rename | Rename… | mdi-pencil-outline | F2 | on",
            "g1 edit abm.edit-notes | Edit notes… | mdi-note-text-outline | - | on",
            "g1 edit abm.move-earlier | Move earlier | mdi-arrow-left | Alt+Up | off (It is already the first of its siblings.)",
            "g1 edit abm.move-later | Move later | mdi-arrow-right | Alt+Down | on",
            "g1 edit abm.remove | Remove | mdi-delete-outline | Delete | on",
            "g2 arrange abm.arrange | Arrange diagram | mdi-sitemap-outline | - | on",
        ], lines);
    }

    [Fact]
    public void ANodeThatTakesAChild_OffersEachKindUnderIt_ByTheKindsMenuName()
    {
        var diagram = Derivations.BehaviorDiagram();

        var groups = BehaviorModel(DislMenuTarget.Element(Derivations.Element(diagram, "1.2")));

        Assert.Equal(["edit", "add", "arrange"], groups.Select(group => group.Name));
        var adds = groups[1].Entries;
        Assert.Equal(11, adds.Count);
        Assert.Equal("abm.add.sequence | Add child: Do in order", $"{adds[0].Id} | {adds[0].Label}");
        Assert.Equal("abm.add.repeat | Add child: Repeat until", $"{adds[4].Id} | {adds[4].Label}");
        Assert.Equal("RepeatUntil", adds[4].Arguments["kind"]);
        Assert.Equal("abm.move-later | off (It is already the last of its siblings.)", $"{groups[0].Entries[3].Id} | {(groups[0].Entries[3].Available ? "on" : "off")} ({groups[0].Entries[3].UnavailableReason})");
    }

    [Fact]
    public void TheBehaviorModelsCanvas_OffersEachKindHere_AndArrange()
    {
        var groups = BehaviorModel(DislMenuTarget.Canvas(Derivations.BehaviorDiagram()));

        Assert.Equal([11, 1], groups.Select(group => group.Entries.Count));
        Assert.Equal("abm.add.ask | Add Ask the user here", $"{groups[0].Entries[9].Id} | {groups[0].Entries[9].Label}");
        Assert.Equal("abm.arrange", groups[1].Entries[0].Id);
    }

    [Fact]
    public void AConnectionOnADerivedRelation_RunsItsConnectEdit()
    {
        var diagram = Derivations.BehaviorDiagram();

        var groups = BehaviorModel(DislMenuTarget.Connection(diagram, Derivations.Element(diagram, "1.2"), Derivations.Element(diagram, "1.1"), "Child"));

        Assert.Equal(["g1  abm.connect.child | Move under this node | mdi-file-tree-outline | - | on"], Lines(groups));
    }

    [Theory]
    [InlineData("F2", "F2", false, false, false, false)]
    [InlineData("Alt+Up", "Up", false, false, true, false)]
    [InlineData("Ctrl+Shift+Z", "Z", true, true, false, false)]
    [InlineData("Cmd+Plus", "Plus", false, false, false, true)]
    public void AShortcut_IsReadAsModifiersThenItsKey(string text, string key, bool ctrl, bool shift, bool alt, bool meta) =>
        Assert.Equal(new DerivedShortcut(text, key, ctrl, shift, alt, meta), DerivedShortcut.Parse(text));

    [Fact]
    public void Groups_FollowTheDeclaredEntries_AndAHiddenOnesGroupIsDropped()
    {
        var specification = Specifications.Loaded(Specifications.With("""
            "toolbox": { "contextMenus": [ { "for": [ "Thing" ], "tools": [
              { "kind": "editLabel", "label": "A", "x-menu.group": "one" },
              { "kind": "delete", "label": "B", "x-menu.group": "two", "visible": "false" },
              { "kind": "duplicate", "label": "C", "x-menu.group": "one" },
              { "kind": "duplicate", "label": "D", "x-menu.group": "one" },
              { "kind": "openForm", "label": "E" } ] } ] }
            """));
        var diagram = new DislDiagram(specification);
        var thing = diagram.AddNode("Thing", "t");

        var groups = ContextMenuDerivation.Derive(specification, DislMenuTarget.Element(thing), new DislEnv(), WireIdMap.None);

        Assert.Equal(["one:A", "one:C,D", ":E"], groups.Select(group => $"{group.Name}:{string.Join(",", group.Entries.Select(entry => entry.Label))}"));
    }
}
