using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Registration;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using EtAlii.Adp.Specification.Fbl.Tests.Support;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Reading;

/// <summary>Rule precedence, ids, headers and findings (FBL §5, §7.4, §7.5, §8.1, §8.5, §8.6; Requirements 4, 5.8 and 7).</summary>
public class ReadingTests
{
    private static readonly FblBinding _timeline = RealFileCorpus.Binding("timeline.fbl", "timeline");

    private static FblBinding Inline(string elements, string extra = "")
    {
        var json = $$"""{ "fbl": "0.1", "bindings": { "t": { "claims": { "extensions": [".t"] }, "body": { "kind": "file", "family": "yaml" }, "reader": "declared", "elements": {{elements}}{{extra}} } } }""";
        var problems = FblDocumentLoader.Load(Encoding.UTF8.GetBytes(json), null, out var document);
        Assert.Empty(problems);
        return document!.Bindings["t"];
    }

    private static FblModel Read(string body, FblBinding binding, FblOptions? options = null) =>
        OpenBody.Open(Encoding.UTF8.GetBytes(body), binding, options ?? new FblOptions { FileName = "plan.t" }).Model;

    [Fact]
    public void TheFirstRuleInBindingOrderTakesAnEntryAndAnEntryBecomesOneElement()
    {
        // Arrange: both rules select every item; only the second's when excludes nothing.
        var binding = Inline("""
            [
              { "name": "special", "type": "Special", "at": "/items/*", "when": "has(entry.special)", "id": { "from": { "key": "id" } } },
              { "name": "plain", "type": "Plain", "at": "/items/*", "id": { "from": { "key": "id" } } }
            ]
            """);

        // Act.
        var model = Read("items:\n  - id: a\n    special: true\n  - id: b\n", binding);

        // Assert.
        Assert.Equal(["a:Special", "b:Plain"], model.Elements.Select(e => $"{e.Id}:{e.Type}"));
    }

    [Fact]
    public void AnEntryWithoutAnIdIsAddressedByItsPlaceAndMarkedNotStored()
    {
        // Arrange.
        var binding = Inline("""[{ "name": "item", "type": "Item", "at": "/items/*" }]""");

        // Act.
        var model = Read("items:\n  - label: a\n", binding);

        // Assert.
        Assert.False(Assert.Single(model.Elements).IdIsStored);
    }

    [Fact]
    public void ASidecarIdIsTakenFromTheRegistrationsIdentities()
    {
        // Arrange.
        var binding = Inline("""[{ "name": "item", "type": "Item", "at": "/items/*", "id": { "sidecar": { "key": "entry.label" } }, "attributes": { "label": { "key": "label" } } }]""");
        var options = new FblOptions { FileName = "plan.t", Identities = new Dictionary<string, string> { ["Tea"] = "c1" } };

        // Act.
        var model = Read("items:\n  - label: Tea\n  - label: Cup\n", binding, options);

        // Assert: the stored identity is used; an element without one is not given a stored id.
        Assert.Equal("c1", model.Elements[0].Id);
        Assert.True(model.Elements[0].IdIsStored);
        Assert.False(model.Elements[1].IdIsStored);
    }

    [Fact]
    public void TheSecondOfTwoEqualIdsIsReportedAndNotStored()
    {
        // Act.
        var model = Read("elements:\n  - id: a\n    label: One\n    begin: 2026-01-01\n  - id: a\n    label: Two\n    begin: 2026-02-01\n", _timeline);

        // Assert.
        var duplicate = Assert.Single(model.Findings, f => f.Code == FindingCodes.DuplicateId);
        Assert.Equal(5, duplicate.Location!.Line);
        Assert.True(model.Elements[0].IdIsStored);
        Assert.False(model.Elements[1].IdIsStored);
        Assert.NotEqual("a", model.Elements[1].Id);
    }

    [Fact]
    public void AMissingHeaderMarkIsReportedAndTheBodyIsStillRead()
    {
        // Act.
        var model = Read("elements:\n  - id: a\n    label: One\n    begin: 2026-01-01\n", _timeline);

        // Assert.
        Assert.Contains(model.Findings, f => f.Code == FindingCodes.HeaderMismatch);
        Assert.Single(model.Elements);
    }

    [Fact]
    public void ARequiredHeaderThatIsMissingMakesTheBodyUnreadableWithOneFinding()
    {
        // Arrange.
        var mindmap = RealFileCorpus.Binding("mindmap.fbl", "freeplane");

        // Act.
        var body = OpenBody.Open("<notamap/>"u8.ToArray(), mindmap, new FblOptions { FileName = "plan.mm" });

        // Assert.
        Assert.True(body.Model.Unreadable);
        Assert.True(body.IsReadOnly);
        Assert.Empty(body.Model.Elements);
        Assert.Equal(FindingCodes.Unparseable, Assert.Single(body.Model.Findings).Code);
    }

    [Fact]
    public void AnEntryARuleMatchesButCannotReadIsReportedAndKept()
    {
        // Arrange: the when expression fails on an entry whose count is not a number.
        var binding = Inline("""[{ "name": "item", "type": "Item", "at": "/items/*", "when": "int(entry.count) > 0", "id": { "from": { "key": "id" } } }]""");
        var text = "items:\n  - id: a\n    count: 3\n  - id: b\n    count: many\n";

        // Act.
        var body = OpenBody.Open(Encoding.UTF8.GetBytes(text), binding, new FblOptions { FileName = "plan.t" });

        // Assert.
        var unreadable = Assert.Single(body.Model.Findings, f => f.Code == FindingCodes.UnreadableEntry);
        Assert.Equal(("plan.t", 4, 3), (unreadable.Location!.File, unreadable.Location.Line, unreadable.Location.Column));
        Assert.True(unreadable.Location.Length > 0);
        Assert.Equal("a", Assert.Single(body.Model.Elements).Id);
        Assert.False(body.Model.Unreadable);
        Assert.Equal(text, Encoding.UTF8.GetString(body.Bytes));
    }

    [Fact]
    public void AStatementNoRuleReadsIsReportedWhenTheBindingAsksForIt()
    {
        // Arrange.
        var causalLoop = RealFileCorpus.Binding("causal-loop-diagram.fbl", "cld");

        // Act.
        var model = Read("causal-loop\n\nthis is not a statement\n", causalLoop, new FblOptions { FileName = "loop.cld" });

        // Assert.
        var unbound = Assert.Single(model.Findings, f => f.Code == FindingCodes.UnboundStatement);
        Assert.Equal(new SourceLocation("loop.cld", 3, 1, 23), unbound.Location);
    }

    [Fact]
    public void PlanningIsDeterministic()
    {
        // Arrange.
        var text = "timeline: 1\nelements:\n  - id: a\n    label: One\n    begin: 2026-01-01\n";
        var change = new ModelChange.Set("a", new Dictionary<string, object?> { ["label"] = "Two: and more" });

        // Act.
        var first = OpenBody.Open(Encoding.UTF8.GetBytes(text), _timeline).Plan(change);
        var second = OpenBody.Open(Encoding.UTF8.GetBytes(text), _timeline).Plan(change);

        // Assert.
        Assert.Equal(
            Assert.IsType<PlanResult.Planned>(first).Edit.Splices,
            Assert.IsType<PlanResult.Planned>(second).Edit.Splices);
    }

    [Fact]
    public void AnUnknownRegistrationHeaderIsKeptAndReported()
    {
        // Arrange.
        var bytes = "generic/timeline\r\nbody: plan.tml\r\ncolour: green\r\n"u8.ToArray();

        // Act.
        var registration = RegistrationDocument.Read(bytes);
        var findings = registration.UnknownHeaders([], "plan.adp");

        // Assert.
        var unknown = Assert.Single(findings);
        Assert.Equal(FindingCodes.UnknownHeader, unknown.Code);
        Assert.Equal(3, unknown.Location!.Line);
        Assert.Empty(registration.UnknownHeaders(["colour"], "plan.adp"));
        Assert.Equal(bytes, OpenRegistration.Open(bytes).Bytes);
    }

    [Fact]
    public void AStaleLayoutEntryIsReportedAndRemovedAtTheNextWrite()
    {
        // Arrange.
        var registration = OpenRegistration.Open("generic/timeline\r\nlayout:\r\n  a: 1 2\r\n  gone: 3 4\r\n"u8.ToArray());
        registration.KnownIds = new HashSet<string> { "a" };

        // Act.
        var stale = registration.Document.StaleEntries(registration.KnownIds, "plan.adp");
        registration.Change(new ModelChange.Place("a", 5, 6));

        // Assert.
        Assert.Equal(FindingCodes.StaleViewData, Assert.Single(stale).Code);
        Assert.Equal("generic/timeline\r\nlayout:\r\n  a: 5 6\r\n", Encoding.UTF8.GetString(registration.Bytes));
    }

    [Fact]
    public void ABlockThatNamesAViewIsReadAsThatViewWithItsBlockAndPlace()
    {
        // Arrange: the Structurizr workspace of the vendored structurizr-open-block fixture.
        var binding = RealFileCorpus.Binding("structurizr.fbl", "workspace");
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Conformance, "fixtures", "structurizr-open-block", "shop.dsl"));
        var text = Encoding.UTF8.GetString(bytes);
        var start = text.IndexOf("container shop \"containers\" {", StringComparison.Ordinal);
        var end = text.IndexOf('}', text.IndexOf("autoLayout lr", start, StringComparison.Ordinal)) + 1;

        // Act.
        var model = OpenBody.Open(bytes, binding, new FblOptions { FileName = "shop.dsl" }).Model;

        // Assert: the view block on line 9, from its first byte to the brace that closes it.
        var view = Assert.Single(model.Views);
        Assert.Equal("containers", view.Name);
        Assert.Equal("view", view.Block);
        Assert.Equal(9, view.Line);
        Assert.Equal(new Span(start, end), view.Span);
    }

    [Fact]
    public void AnIdStrategyIsAskedWithTheTypeAndLineOfEveryEntryWithoutAStoredId()
    {
        // Arrange: the causal loop diagram of the vendored causal-loop-rename fixture, whose links and loop store no id.
        var binding = RealFileCorpus.Binding("causal-loop-diagram.fbl", "cld");
        var bytes = File.ReadAllBytes(Path.Combine(Repository.Conformance, "fixtures", "causal-loop-rename", "growth.cld"));
        var requests = new List<IdRequest>();

        // Act.
        _ = OpenBody.Open(bytes, binding, new FblOptions { FileName = "growth.cld", DeriveId = request => { requests.Add(request); return null; } }).Model;

        // Assert: the two links on lines 7 and 8 and the loop on line 10.
        Assert.Equal(
            ["CausalLink@7", "CausalLink@8", "Loop@10"],
            requests.Select(r => $"{r.Type}@{r.Line}").Order(StringComparer.Ordinal));
    }
}
