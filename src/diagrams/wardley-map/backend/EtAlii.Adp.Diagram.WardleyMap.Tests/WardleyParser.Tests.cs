using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyParserTests
{
    private static string FixturesPath => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static WardleyMap ParseFixture(string name) =>
        WardleyParser.Parse(WardleyDocument.Parse(File.ReadAllText(IoPath.Combine(FixturesPath, name))));

    private static WardleyMap ParseText(string text) => WardleyParser.Parse(WardleyDocument.Parse(text));

    public static TheoryData<string> Corpus
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var path in Directory.EnumerateFiles(FixturesPath, "*.owm").Order(StringComparer.Ordinal))
            {
                data.Add(IoPath.GetFileName(path));
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Corpus))]
    public void Parse_NeverThrows_ForAnythingInTheCorpus(string name)
    {
        // Act.
        var map = ParseFixture(name);

        // Assert. Parsing and judging are separate: a map with a dangling link or an unknown
        // keyword still reads, and the validator reports it later (Requirements 3.5, 14).
        Assert.NotNull(map);
    }

    [Fact]
    public void Parse_ReadsTheThreeStatementKinds()
    {
        // Arrange.
        const string text = """
            component Alpha [0.80, 0.40]
            anchor Customer [0.95, 0.63]
            submap Billing [0.45, 0.68]
            """;

        // Act.
        var map = ParseText(text);

        // Assert.
        Assert.Equal(
            [WardleyElementKind.Component, WardleyElementKind.Anchor, WardleyElementKind.Submap],
            map.Components.Select(component => component.Kind));
    }

    [Fact]
    public void Parse_ReadsMarketAndEcosystemAsDecorators_NotAsKinds()
    {
        // Arrange. The correction the task 2 corpus forced: `market Foo [..]` is a parse error
        // in the real parser, and `component Foo [..] (market)` is the syntax.
        const string text = """
            component BetaMarket [0.66, 0.88] (market)
            component GammaEco [0.40, 0.47] (ecosystem)
            """;

        // Act.
        var map = ParseText(text);

        // Assert.
        Assert.All(map.Components, component => Assert.Equal(WardleyElementKind.Component, component.Kind));
        Assert.Equal([WardleyDecorator.Market], map.Components[0].Decorators);
        Assert.Equal([WardleyDecorator.Ecosystem], map.Components[1].Decorators);
    }

    [Fact]
    public void Parse_IgnoresAMarketKeywordStatement_BecauseTheFormatHasNoSuchStatement()
    {
        // Arrange.
        const string text = """
            component Alpha [0.80, 0.40]
            market BetaMarket [0.66, 0.88]
            """;

        // Act.
        var map = ParseText(text);

        // Assert. One component, not two - matching what the real parser does, which is to
        // report a ParseError and yield one component. The line itself survives in the
        // document, so a round trip still returns it (Requirement 3.3).
        Assert.Equal(["Alpha"], map.Components.Select(component => component.Name));
    }

    [Fact]
    public void Parse_ReadsCoordinatesAsVisibilityThenMaturity()
    {
        // Act.
        var map = ParseText("component Alpha [0.80, 0.40]");

        // Assert. Requirement 5.2, and the module's most error-prone single line. Confirmed
        // against the real parser in the task 2 certification.
        Assert.Equal(0.80d, map.Components[0].Position.Visibility);
        Assert.Equal(0.40d, map.Components[0].Position.Maturity);
    }

    [Fact]
    public void Parse_ReadsAllFiveDecoratorsAndInertia()
    {
        // Arrange.
        const string text = """
            component A [0.5, 0.5] (build)
            component B [0.5, 0.5] (buy)
            component C [0.5, 0.5] (outsource)
            component D [0.5, 0.5] (market)
            component E [0.5, 0.5] (ecosystem)
            component F [0.5, 0.5] inertia
            """;

        // Act.
        var map = ParseText(text);

        // Assert.
        Assert.Equal([WardleyDecorator.Build], map.Components[0].Decorators);
        Assert.Equal([WardleyDecorator.Buy], map.Components[1].Decorators);
        Assert.Equal([WardleyDecorator.Outsource], map.Components[2].Decorators);
        Assert.Equal([WardleyDecorator.Market], map.Components[3].Decorators);
        Assert.Equal([WardleyDecorator.Ecosystem], map.Components[4].Decorators);

        // inertia is a boolean, not a decorator (Requirement 6.2).
        Assert.True(map.Components[5].Inertia);
        Assert.Empty(map.Components[5].Decorators);
    }

    [Fact]
    public void Parse_IgnoresAnUnknownDecorator_RatherThanRefusingTheLine()
    {
        // Act. The reference parser tolerates one from a newer DSL silently; the document keeps
        // the text either way (Requirement 6.3).
        var map = ParseText("component Future [0.5, 0.5] (someDecoratorFromLater)");

        // Assert.
        Assert.Single(map.Components);
        Assert.Empty(map.Components[0].Decorators);
    }

    [Theory]
    [InlineData("A->B", WardleyLinkKind.Dependency, "")]
    [InlineData("A+>B", WardleyLinkKind.Flow, "")]
    [InlineData("A->B; via the plugin SDK", WardleyLinkKind.Dependency, "via the plugin SDK")]
    public void Parse_ReadsLinkForms(string text, WardleyLinkKind kind, string context)
    {
        // Act.
        var map = ParseText(text);

        // Assert.
        var link = Assert.Single(map.Links);
        Assert.Equal("A", link.Source);
        Assert.Equal("B", link.Target);
        Assert.Equal(kind, link.Kind);
        Assert.Equal(context, link.Context);
    }

    [Fact]
    public void Parse_ReadsLinkEndpointsWithSpacesInTheirNames()
    {
        // Act. Names with spaces are the norm in this notation, not an edge case.
        var map = ParseText("Cup of Tea->Hot Water");

        // Assert.
        var link = Assert.Single(map.Links);
        Assert.Equal("Cup of Tea", link.Source);
        Assert.Equal("Hot Water", link.Target);
    }

    [Fact]
    public void Parse_ReadsTheNestedPipelineForm_WithChildrenCarryingMaturityOnly()
    {
        // Arrange.
        const string text = """
            component Kettle [0.43, 0.35]
            pipeline Kettle
            {
              component Campfire Kettle [0.15]
              component Electric Kettle [0.63]
            }
            """;

        // Act.
        var map = ParseText(text);

        // Assert.
        var pipeline = Assert.Single(map.Pipelines);
        Assert.Equal("Kettle", pipeline.Parent);
        Assert.Equal(WardleyPipelineForm.Nested, pipeline.Form);
        Assert.Equal(["Campfire Kettle", "Electric Kettle"], pipeline.Children.Select(child => child.Name));
        Assert.Equal([0.15d, 0.63d], pipeline.Children.Select(child => child.Maturity));
    }

    [Fact]
    public void Parse_ReadsTheLegacyPipelineForm_AndRemembersItWasLegacy()
    {
        // Act. Requirement 5.4 - written back in whichever form it was read.
        var map = ParseText("pipeline Power [0.30, 0.85]");

        // Assert.
        var pipeline = Assert.Single(map.Pipelines);
        Assert.Equal("Power", pipeline.Parent);
        Assert.Equal(WardleyPipelineForm.Legacy, pipeline.Form);
        Assert.Empty(pipeline.Children);
        Assert.Equal(new WardleyCoordinate(0.30d, 0.85d), pipeline.LegacyExtent);
    }

    [Fact]
    public void Parse_ReadsBothPipelineFormsInOneDocument()
    {
        // Act.
        var map = ParseFixture("pipelines-both-forms.owm");

        // Assert. Three pipelines: nested, legacy, and an empty nested one.
        Assert.Equal(3, map.Pipelines.Count);
        Assert.Contains(map.Pipelines, pipeline => pipeline.Form == WardleyPipelineForm.Nested);
        Assert.Contains(map.Pipelines, pipeline => pipeline.Form == WardleyPipelineForm.Legacy);
    }

    [Fact]
    public void Parse_ToleratesAnEmptyPipeline()
    {
        // Arrange.
        const string text = """
            component Water [0.38, 0.82]
            pipeline Water
            {
            }
            """;

        // Act.
        var map = ParseText(text);

        // Assert.
        Assert.Empty(Assert.Single(map.Pipelines).Children);
    }

    [Fact]
    public void Parse_ReadsLabelOffsetsAndUrls()
    {
        // Arrange.
        const string text = """
            component Kettle [0.43, 0.35] label [-57, 4]
            submap Site [0.83, 0.50] url(siteUrl)
            """;

        // Act.
        var map = ParseText(text);

        // Assert. The offset is in pixels, and negative - a property of the format.
        Assert.Equal(new WardleyLabelOffset(-57d, 4d), map.Components[0].LabelOffset);
        Assert.Equal("siteUrl", map.Components[1].Url);
    }

    [Fact]
    public void Parse_ReadsTheMapLevelStatements()
    {
        // Arrange.
        const string text = """
            title Strategy vocabulary
            size [900,700]
            style wardley
            """;

        // Act.
        var map = ParseText(text);

        // Assert. Properties of the map, not elements on it (Requirement 5.6).
        Assert.Equal("Strategy vocabulary", map.Title);
        Assert.Equal(new WardleyMapSize(900d, 700d), map.Size);
        Assert.Equal("wardley", map.Style);
    }

    [Fact]
    public void Parse_RecordsTheDeclaringLine_OneBased()
    {
        // Arrange.
        const string text = """
            title Lines
            component Alpha [0.80, 0.40]
            """;

        // Act.
        var map = ParseText(text);

        // Assert. 1-based, matching DiagramProblemLineLocation - the reference parser's own
        // numbering is 0-based, which is why this is asserted rather than assumed.
        Assert.Equal(2u, map.Components[0].Line);
    }

    [Fact]
    public void Parse_IgnoresComments_IncludingTrailingOnes()
    {
        // Act.
        var map = ParseFixture("comments-everywhere.owm");

        // Assert. The anchor and both components survive, and two links; no comment becomes an
        // element, and a trailing comment does not become part of the name before it.
        Assert.Equal(["Customer", "Cup of Tea", "Kettle"], map.Components.Select(component => component.Name));
        Assert.Equal(2, map.Links.Count);
        Assert.Equal("Kettle", map.Links[1].Target);
    }

    [Fact]
    public void Parse_SkipsStatementsItDoesNotModel_WithoutLosingTheOnesItDoes()
    {
        // Act.
        var map = ParseFixture("unmodelled-statements.owm");

        // Assert. Requirement 3.3 - the unknown lines are the document's business, not the
        // model's, and they must not take the modelled statements down with them.
        Assert.Contains(map.Components, component => component.Name == "Cup of Tea");
        Assert.Single(map.Links);
    }

    [Fact]
    public void Parse_ReadsTheTeaShopEndToEnd()
    {
        // Act.
        var map = ParseFixture("tea-shop.owm");

        // Assert. The shape every Wardley map has: two anchors, a component chain, eight links.
        Assert.Equal("Tea shop", map.Title);
        Assert.Equal(2, map.Components.Count(component => component.Kind == WardleyElementKind.Anchor));
        Assert.Equal(7, map.Components.Count(component => component.Kind == WardleyElementKind.Component));
        Assert.Equal(8, map.Links.Count);
    }

    [Fact]
    public void Parse_ReadsTheStrategyVocabularyEndToEnd()
    {
        // Act.
        var map = ParseFixture("strategy-vocabulary.owm");

        // Assert.
        Assert.Contains(map.Components, component => component.Decorators.Contains(WardleyDecorator.Market));
        Assert.Contains(map.Components, component => component.Decorators.Contains(WardleyDecorator.Ecosystem));
        Assert.Contains(map.Components, component => component.Decorators.Contains(WardleyDecorator.Build));
        Assert.Contains(map.Components, component => component.Decorators.Contains(WardleyDecorator.Buy));
        Assert.Contains(map.Components, component => component.Decorators.Contains(WardleyDecorator.Outsource));
        Assert.Contains(map.Components, component => component.Inertia);
        Assert.Contains(map.Links, link => link.Kind == WardleyLinkKind.Flow);
        Assert.Contains(map.Links, link => link.Context.Length > 0);
    }

    [Theory]
    [InlineData("y_axis Value chain->Invisible->Visible")]
    [InlineData("evolve Datacentre->Cloud Hosting 0.83")]
    public void Parse_DoesNotMistakeAKeywordStatementForALink_EvenWhenItContainsAnArrow(string text)
    {
        // Arrange. Found by the corpus: `y_axis` contains two arrows and was being read as a
        // link, which would put an element on the canvas the document never declared - a worse
        // failure than not modelling the statement at all.
        // Act.
        var map = ParseText(text);

        // Assert.
        Assert.Empty(map.Links);
    }

    [Fact]
    public void Parse_ReadsAnEmptyDocumentAsAnEmptyMap()
    {
        // Act.
        var map = ParseText("");

        // Assert. Requirement 2.4 - a missing sibling opens as an empty map, not an error.
        Assert.Empty(map.Components);
        Assert.Empty(map.Links);
        Assert.Equal("", map.Title);
    }

    [Fact]
    public void Parse_ReadsTheFactorysEmptyDocument()
    {
        // Arrange. What Add writes (Requirement 1.4) must read back as a valid, titled map.
        var body = new WardleyDocumentFactory().CreateEmptyDocument("map");

        // Act.
        var map = ParseText(body);

        // Assert.
        Assert.Equal("map", map.Title);
        Assert.Empty(map.Components);
    }

    [Fact]
    public void Parse_UsesInvariantNumberParsing()
    {
        // Act. A machine whose locale uses a comma decimal separator must still read 0.79 as
        // seventy-nine hundredths rather than as seventy-nine.
        var map = ParseText("component Alpha [0.79, 0.61]");

        // Assert.
        Assert.Equal(0.79d, map.Components[0].Position.Visibility);
    }

    [Fact]
    public void Parse_RejectsNull()
    {
        // Act and assert.
        Assert.Throws<ArgumentNullException>(() => WardleyParser.Parse(null!));
    }
}
