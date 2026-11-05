using System.Globalization;
using System.Text;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.History;

/// <summary>
/// A body whose ids are kept in its legacy identities file (FBL 0.12 §8.6, §8.7), beyond what the <c>wardley-*</c> conformance
/// fixtures show: an id stored for the first time is no step of the history, and an entry taken away hands its id to no other.
/// </summary>
public class IdentifiedBodyTests
{
    /// <summary>The DISL id strategy of these tests: an element read without an entry is known by its line, as <c>d3</c>.</summary>
    private static readonly FblOptions Options = new()
    {
        FileName = "map.owm",
        DeriveId = request => "d" + request.Line.ToString(CultureInfo.InvariantCulture),
    };

    private static IdentifiedBody Open(string body, string identities) =>
        IdentifiedBody.Open(Encoding.UTF8.GetBytes(body), RealFileCorpus.Binding("wardley-map.fbl", "map"), Options, LegacySidecar.Open(Encoding.UTF8.GetBytes(identities)));

    private static string Entry(string id, string kind, string key) => $"  {{\n    \"id\": \"{id}\",\n    \"kind\": \"{kind}\",\n    \"key\": \"{key}\"\n  }}";

    private static string IdOf(IdentifiedBody body, string type, string key) =>
        body.Body.Model.Elements.Single(element => element.Type == type && element.IdentityKey == key).Id;

    [Fact]
    public void TheLegacyIdentitiesFileABindingNames_IsReadable_AndOneThatNamesNoneHasNone()
    {
        // Arrange.
        var wardley = RealFileCorpus.Binding("wardley-map.fbl", "map");
        var turtle = RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");

        // Act.
        var path = LegacySidecar.PathFor(wardley.LegacyIdentities!, Path.Combine("maps", "tea.owm"));

        // Assert: the pattern is the binding's, resolved beside the body (FBL 0.12 §8.7).
        Assert.Equal("{base}.identities.json", wardley.LegacyIdentities);
        Assert.Equal("tea.identities.json", Path.GetFileName(path));
        Assert.Null(turtle.LegacyIdentities);
    }

    [Fact]
    public void ARenameOfALaterHolder_RewritesNoReference_SinceEachNamesTheFirstHolder()
    {
        // Arrange: two components named Tea; the link and the evolution naming Tea attach to the first (FBL 5.3, 5.4).
        var body = Open(
            "component Tea [0.1, 0.1]\ncomponent Tea [0.2, 0.2]\ncomponent Cup [0.8, 0.2]\nTea->Cup\nevolve Tea 0.9\n",
            "[\n" + Entry("c1", "component", "Tea") + ",\n" + Entry("c2", "component", "Tea\\u001F2\\t") + "\n]\n");

        // Act: the later holder is renamed, and then the first.
        var later = body.Change(new ModelChange.Set("c2", new Dictionary<string, object?> { ["name"] = "Leaf" }));
        var afterLater = Encoding.UTF8.GetString(body.Body.Bytes);
        var first = body.Change(new ModelChange.Set("c1", new Dictionary<string, object?> { ["name"] = "Kettle" }));

        // Assert: the later holder's own statement alone changes; the first holder takes every reference with it (FBL 5.7).
        Assert.IsType<PlanResult.Planned>(later.Body);
        Assert.Equal("component Tea [0.1, 0.1]\ncomponent Leaf [0.2, 0.2]\ncomponent Cup [0.8, 0.2]\nTea->Cup\nevolve Tea 0.9\n", afterLater);
        Assert.IsType<PlanResult.Planned>(first.Body);
        Assert.Equal("component Kettle [0.1, 0.1]\ncomponent Leaf [0.2, 0.2]\ncomponent Cup [0.8, 0.2]\nKettle->Cup\nevolve Kettle 0.9\n", Encoding.UTF8.GetString(body.Body.Bytes));
    }

    [Fact]
    public void AnIdStoredForTheFirstTime_IsWrittenWithTheEdit_AndIsNoStepOfItsHistory()
    {
        // Arrange: Tea has an entry, Cup has none and is known by the id the reading gives it.
        var body = Open("title T\ncomponent Tea [0.5, 0.5]\ncomponent Cup [0.4, 0.4]\n", "[\n" + Entry("c1", "component", "Tea") + "\n]\n");
        Assert.Equal("d3", IdOf(body, "Component", "Cup"));

        // Act: a rename of Tea, which moves its entry's key, and its undo.
        var edit = body.Change(new ModelChange.Set("c1", new Dictionary<string, object?> { ["name"] = "Kettle" }));
        var afterEdit = Encoding.UTF8.GetString(body.Identities.Bytes);
        (UndoResult undone, IReadOnlyList<Splice> identities) = body.Undo();

        // Assert: Cup's entry is written with the edit, as no splice of its step (FBL 0.12 §8.6); the undo moves Tea's key back
        // and leaves Cup's entry, so Cup keeps the id it is known by.
        Assert.IsType<PlanResult.Planned>(edit.Body);
        Assert.Equal("\"Kettle\"", Assert.Single(edit.Identities.Splices).Text);
        Assert.Equal("[\n" + Entry("c1", "component", "Kettle") + ",\n" + Entry("d3", "component", "Cup") + "\n]\n", afterEdit);
        Assert.IsType<UndoResult.Done>(undone);
        Assert.Single(identities);
        Assert.Equal("[\n" + Entry("c1", "component", "Tea") + ",\n" + Entry("d3", "component", "Cup") + "\n]\n", Encoding.UTF8.GetString(body.Identities.Bytes));
        Assert.Equal("c1", IdOf(body, "Component", "Tea"));
        Assert.Equal("d3", IdOf(body, "Component", "Cup"));
    }

    [Fact]
    public void AnEntryTakenAway_HandsItsIdToNoOther()
    {
        // Arrange: two notes, the first with an entry; the second starts where the first did once the first is taken away.
        var body = Open("note Alpha [0.1, 0.1]\nnote Beta [0.2, 0.2]\n", "[\n" + Entry("n1", "note", "Alpha") + "\n]\n");
        Assert.Equal("d2", IdOf(body, "Note", "Beta"));

        // Act.
        var edit = body.Change(new ModelChange.Remove("n1"));

        // Assert: the first's entry is pruned and the second keeps the id it was known by, never the first's.
        Assert.IsType<PlanResult.Planned>(edit.Body);
        Assert.Equal("d2", IdOf(body, "Note", "Beta"));
        Assert.DoesNotContain("n1", Encoding.UTF8.GetString(body.Identities.Bytes), StringComparison.Ordinal);
    }
}
