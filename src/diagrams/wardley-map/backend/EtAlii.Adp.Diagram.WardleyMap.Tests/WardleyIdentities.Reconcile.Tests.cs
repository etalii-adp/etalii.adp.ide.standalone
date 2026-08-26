using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// Matching recorded identities to a parsed map (Requirements 4.3, 4.5) - the half of task 9
/// that had to wait for the parser.
/// </summary>
public class WardleyIdentitiesReconcileTests
{
    private static WardleyMap ParseText(string text) => WardleyParser.Parse(WardleyDocument.Parse(text));

    private static WardleyMap Fixture(string name) =>
        WardleyParser.Parse(WardleyDocument.Parse(
            File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name))));

    [Fact]
    public void Reconcile_AssignsAnIdToEveryElement_WhenNothingIsRecorded()
    {
        // Arrange. Requirement 4.3 - a map authored elsewhere arrives with no sidecar.
        var map = Fixture("tea-shop.owm");

        // Act.
        var entries = WardleyIdentities.Reconcile(map, []);

        // Assert. Nine components and eight links.
        Assert.Equal(map.Components.Count + map.Links.Count, entries.Count);
        Assert.All(entries, entry => Assert.NotEmpty(entry.Id));
    }

    [Fact]
    public void Reconcile_GivesEveryElementADistinctId()
    {
        // Act.
        var entries = WardleyIdentities.Reconcile(Fixture("strategy-vocabulary.owm"), []);

        // Assert.
        Assert.Equal(entries.Count, entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Reconcile_KeepsTheRecordedIdForAnElementItStillRecognises()
    {
        // Arrange.
        var map = ParseText("component Cup of Tea [0.79, 0.61]\n");
        var first = WardleyIdentities.Reconcile(map, []);

        // Act.
        var second = WardleyIdentities.Reconcile(map, first);

        // Assert. Identity has to outlive a reload, or a selection and every undo entry
        // mentioning the element break on every open.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Reconcile_KeepsAnIdWhenTheElementMoves()
    {
        // Arrange. A drag changes the coordinates, not what the element is.
        var before = ParseText("component Cup of Tea [0.79, 0.61]\n");
        var recorded = WardleyIdentities.Reconcile(before, []);
        var after = ParseText("component Cup of Tea [0.20, 0.90]\n");

        // Act.
        var entries = WardleyIdentities.Reconcile(after, recorded);

        // Assert.
        Assert.Equal(recorded[0].Id, entries[0].Id);
    }

    [Fact]
    public void Reconcile_KeepsAnIdWhenADecoratorIsAddedOrRemoved()
    {
        // Arrange.
        var before = ParseText("component Payment [0.70, 0.72]\n");
        var recorded = WardleyIdentities.Reconcile(before, []);
        var after = ParseText("component Payment [0.70, 0.72] (buy)\n");

        // Act.
        var entries = WardleyIdentities.Reconcile(after, recorded);

        // Assert.
        Assert.Equal(recorded[0].Id, entries[0].Id);
    }

    [Fact]
    public void Reconcile_DiscardsAnEntryWhoseElementIsGone()
    {
        // Arrange.
        var recorded = WardleyIdentities.Reconcile(ParseText("component Gone [0.5, 0.5]\n"), []);

        // Act.
        var entries = WardleyIdentities.Reconcile(ParseText("component Present [0.5, 0.5]\n"), recorded);

        // Assert. Requirement 4.5 - discard what cannot be matched, and carry on.
        Assert.Equal("Present", Assert.Single(entries).Key);
        Assert.NotEqual(recorded[0].Id, entries[0].Id);
    }

    [Fact]
    public void Reconcile_TreatsARenameAsANewElement_WhichIsWhyRenameRewritesTheKeyItself()
    {
        // Arrange. This is the behaviour Requirement 4.4's rename command exists to avoid: on
        // its own, matching by name cannot see through a rename, so the rename must rewrite the
        // sidecar key in the same command rather than leaving it to be re-derived.
        var recorded = WardleyIdentities.Reconcile(ParseText("component Kettle [0.43, 0.35]\n"), []);

        // Act.
        var entries = WardleyIdentities.Reconcile(ParseText("component Boiler [0.43, 0.35]\n"), recorded);

        // Assert.
        Assert.NotEqual(recorded[0].Id, Assert.Single(entries).Id);
    }

    [Fact]
    public void Reconcile_DistinguishesElementsOfDifferentKindsSharingAKey()
    {
        // Arrange. A component and a note may legitimately hold the same text.
        var map = ParseText("component Renewal [0.5, 0.5]\nnote Renewal [0.2, 0.3]\n");

        // Act.
        var entries = WardleyIdentities.Reconcile(map, []);

        // Assert.
        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Select(entry => entry.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(entries, entry => entry.Kind == WardleyIdentityKind.Component);
        Assert.Contains(entries, entry => entry.Kind == WardleyIdentityKind.Note);
    }

    [Fact]
    public void Reconcile_DistinguishesTwoLinksBetweenTheSamePair()
    {
        // Arrange. A dependency and a flow link between one pair are different links.
        var map = ParseText("Alpha->Beta\nAlpha+>Beta\n");

        // Act.
        var entries = WardleyIdentities.Reconcile(map, []);

        // Assert.
        Assert.Equal(2, entries.Count(entry => entry.Kind == WardleyIdentityKind.Link));
        Assert.Equal(2, entries.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Reconcile_DistinguishesPipelineChildrenSharingANameAcrossPipelines()
    {
        // Arrange.
        const string text = """
            component Kettle [0.4, 0.4]
            pipeline Kettle
            {
              component Electric [0.6]
            }
            component Stove [0.3, 0.3]
            pipeline Stove
            {
              component Electric [0.7]
            }
            """;

        // Act.
        var entries = WardleyIdentities.Reconcile(ParseText(text), []);

        // Assert. The key carries the parent, so two children called Electric stay distinct.
        var children = entries.Where(entry => entry.Kind == WardleyIdentityKind.PipelineChild).ToArray();
        Assert.Equal(2, children.Length);
        Assert.Equal(2, children.Select(entry => entry.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Reconcile_CoversTheStrategyVocabularysNotesAndAnnotations()
    {
        // Act.
        var entries = WardleyIdentities.Reconcile(Fixture("annotations-and-labels.owm"), []);

        // Assert.
        Assert.Contains(entries, entry => entry.Kind == WardleyIdentityKind.Note);
        Assert.Equal(2, entries.Count(entry => entry.Kind == WardleyIdentityKind.Annotation));
    }

    [Fact]
    public void Reconcile_IsStable_SoAnUneditedMapProducesNothingToWrite()
    {
        // Arrange. Requirement 4.3 - a map opened and not edited produces no write at all,
        // which only holds if reconciling twice gives the identical set.
        var map = Fixture("strategy-vocabulary.owm");
        var first = WardleyIdentities.Reconcile(map, []);

        // Act.
        var second = WardleyIdentities.Reconcile(map, first);

        // Assert.
        Assert.Equal(first, second);
    }

    [Fact]
    public void Reconcile_ReturnsNothingForAnEmptyMap()
    {
        // Act.
        var entries = WardleyIdentities.Reconcile(WardleyMap.Empty, []);

        // Assert.
        Assert.Empty(entries);
    }

    [Fact]
    public void Reconcile_RejectsNulls()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => WardleyIdentities.Reconcile(null!, []));
        Assert.Throws<ArgumentNullException>(() => WardleyIdentities.Reconcile(WardleyMap.Empty, null!));
    }
}
