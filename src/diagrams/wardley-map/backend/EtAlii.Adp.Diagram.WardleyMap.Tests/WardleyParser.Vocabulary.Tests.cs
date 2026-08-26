using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

/// <summary>
/// The strategy vocabulary (Requirement 6) - what makes a Wardley map a strategy tool rather
/// than a scatter plot.
/// </summary>
public class WardleyParserVocabularyTests
{
    private static string FixturesPath => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static WardleyMap ParseText(string text) => WardleyParser.Parse(WardleyDocument.Parse(text));

    private static WardleyMap ParseFixture(string name) =>
        WardleyParser.Parse(WardleyDocument.Parse(File.ReadAllText(IoPath.Combine(FixturesPath, name))));

    [Fact]
    public void Parse_ReadsEvolveWithoutARename()
    {
        // Act.
        var map = ParseText("evolve Fraud Scoring 0.72");

        // Assert.
        var evolve = Assert.Single(map.Evolves);
        Assert.Equal("Fraud Scoring", evolve.Name);
        Assert.Equal(0.72d, evolve.Maturity);
        Assert.Equal("", evolve.Override);
    }

    [Fact]
    public void Parse_ReadsEvolveWithARename_AndKeepsTheArrivalName()
    {
        // Act. Requirement 6.1 - `evolve Name->NewName x` also carries the name the component
        // takes when it arrives.
        var map = ParseText("evolve Datacentre->Cloud Hosting 0.83");

        // Assert.
        var evolve = Assert.Single(map.Evolves);
        Assert.Equal("Datacentre", evolve.Name);
        Assert.Equal("Cloud Hosting", evolve.Override);
        Assert.Equal(0.83d, evolve.Maturity);
    }

    [Fact]
    public void Parse_DoesNotReadAnEvolveAsALink_DespiteTheArrow()
    {
        // Act.
        var map = ParseText("evolve Datacentre->Cloud Hosting 0.83");

        // Assert. The arrow is part of the rename, not a dependency.
        Assert.Empty(map.Links);
    }

    [Theory]
    [InlineData("pioneers", WardleyAttitudeKind.Pioneers)]
    [InlineData("settlers", WardleyAttitudeKind.Settlers)]
    [InlineData("townplanners", WardleyAttitudeKind.TownPlanners)]
    public void Parse_ReadsTheThreeAttitudes(string keyword, WardleyAttitudeKind expected)
    {
        // Act.
        var map = ParseText($"{keyword} [0.30, 0.20, 0.55, 0.45]");

        // Assert.
        Assert.Equal(expected, Assert.Single(map.Attitudes).Kind);
    }

    [Fact]
    public void Parse_ReadsAnAttitudesFourNumbersAsVisibilityFirst()
    {
        // Act.
        var map = ParseText("pioneers [0.30, 0.20, 0.55, 0.45]");

        // Assert. Confirmed against the reference parser, which reads these into
        // visibility/maturity/visibility2/maturity2 in that order. Reading them the other way
        // round would put every attitude region on the wrong part of the map while still
        // looking plausible, which is why this is asserted rather than assumed.
        var attitude = Assert.Single(map.Attitudes);
        Assert.Equal(new WardleyCoordinate(0.30d, 0.20d), attitude.From);
        Assert.Equal(new WardleyCoordinate(0.55d, 0.45d), attitude.To);
    }

    [Fact]
    public void Parse_ReadsAcceleratorsAndDeacceleratorsIntoOneCollection()
    {
        // Arrange.
        const string text = """
            accelerator Regulatory pressure [0.70, 0.60]
            deaccelerator Legacy contracts [0.45, 0.30]
            """;

        // Act.
        var map = ParseText(text);

        // Assert. One collection with a flag, as the reference parser models it - splitting
        // them would invent a difference the format does not make.
        Assert.Equal(2, map.Accelerators.Count);
        Assert.False(map.Accelerators[0].IsDeaccelerator);
        Assert.Equal("Regulatory pressure", map.Accelerators[0].Name);
        Assert.True(map.Accelerators[1].IsDeaccelerator);
        Assert.Equal(new WardleyCoordinate(0.45d, 0.30d), map.Accelerators[1].Position);
    }

    [Fact]
    public void Parse_ReadsANote()
    {
        // Act.
        var map = ParseText("note Renewal is due in March [0.22, 0.30]");

        // Assert.
        var note = Assert.Single(map.Notes);
        Assert.Equal("Renewal is due in March", note.Text);
        Assert.Equal(new WardleyCoordinate(0.22d, 0.30d), note.Position);
    }

    [Fact]
    public void Parse_ReadsAnAnnotationPinnedAtOnePlace()
    {
        // Act.
        var map = ParseText("annotation 2 [0.48, 0.85] A single-position annotation");

        // Assert.
        var annotation = Assert.Single(map.Annotations);
        Assert.Equal(2, annotation.Number);
        Assert.Equal([new WardleyCoordinate(0.48d, 0.85d)], annotation.Occurrences);
        Assert.Equal("A single-position annotation", annotation.Text);
    }

    [Fact]
    public void Parse_ReadsAnAnnotationPinnedAtSeveralPlaces_LosingNone()
    {
        // Act. Requirement 6.8's headline case: a one-element-one-position model cannot express
        // this, and none of the positions may be lost.
        var map = ParseText("annotation 1 [[0.43,0.49],[0.08,0.79]] Standardising power");

        // Assert.
        var annotation = Assert.Single(map.Annotations);
        Assert.Equal(1, annotation.Number);
        Assert.Equal(
            [new WardleyCoordinate(0.43d, 0.49d), new WardleyCoordinate(0.08d, 0.79d)],
            annotation.Occurrences);
        Assert.Equal("Standardising power", annotation.Text);
    }

    [Fact]
    public void Parse_ReadsTheAnnotationsBlockPosition_WithoutMistakingItForAnAnnotation()
    {
        // Arrange. `annotations` and `annotation` differ by one character, and the plural is
        // the legend block's position rather than an annotation of its own.
        const string text = """
            annotation 1 [0.43, 0.49] Text
            annotations [0.60, 0.02]
            """;

        // Act.
        var map = ParseText(text);

        // Assert.
        Assert.Single(map.Annotations);
        Assert.Equal(new WardleyCoordinate(0.60d, 0.02d), map.AnnotationsPosition);
    }

    [Fact]
    public void Parse_ReadsAUrlDefinition()
    {
        // Act.
        var map = ParseText("url fulfilmentMap [https://onlinewardleymaps.com/#clone:example]");

        // Assert.
        var url = Assert.Single(map.Urls);
        Assert.Equal("fulfilmentMap", url.Name);
        Assert.Equal("https://onlinewardleymaps.com/#clone:example", url.Address);
    }

    [Fact]
    public void Parse_DoesNotMistakeAUrlSchemeForAComment()
    {
        // Arrange. Found by the corpus: the comment stripper cut every line at `//`, which
        // truncated `url x [https://...]` to `url x [https:` and parsed as nothing.
        const string text = "url siteUrl [https://example.com/map] // and a real comment";

        // Act.
        var map = ParseText(text);

        // Assert. The scheme survives and the trailing comment still goes.
        Assert.Equal("https://example.com/map", Assert.Single(map.Urls).Address);
    }

    [Fact]
    public void Parse_LinksASubmapToItsUrlByName()
    {
        // Act.
        var map = ParseFixture("submaps-and-urls.owm");

        // Assert. Requirement 6.6 - the reference is by name, so one address can serve several
        // elements, and resolving it is the mapper's job rather than the parser's.
        Assert.Equal(2, map.Urls.Count);
        var submaps = map.Components.Where(component => component.Kind == WardleyElementKind.Submap).ToArray();
        Assert.Equal(2, submaps.Length);
        Assert.All(submaps, submap => Assert.Contains(map.Urls, url => url.Name == submap.Url));
    }

    [Fact]
    public void Parse_ReadsTheAnnotationsAndLabelsFixtureWhole()
    {
        // Act.
        var map = ParseFixture("annotations-and-labels.owm");

        // Assert.
        Assert.Equal(2, map.Annotations.Count);
        Assert.Equal(2, map.Annotations.Single(annotation => annotation.Number == 1).Occurrences.Count);
        Assert.Single(map.Annotations.Single(annotation => annotation.Number == 2).Occurrences);
        Assert.NotNull(map.AnnotationsPosition);
        Assert.Single(map.Notes);
        Assert.Equal(3, map.Components.Count(component => component.LabelOffset is not null));
    }

    [Fact]
    public void Parse_ReadsTheStrategyVocabularyFixtureWhole()
    {
        // Act. The fixture that exists to exercise Requirement 6 end to end.
        var map = ParseFixture("strategy-vocabulary.owm");

        // Assert.
        Assert.Equal(2, map.Evolves.Count);
        Assert.Contains(map.Evolves, evolve => evolve.Override == "Cloud Hosting");
        Assert.Equal(3, map.Attitudes.Count);
        Assert.Equal(2, map.Accelerators.Count);
        Assert.Contains(map.Accelerators, accelerator => accelerator.IsDeaccelerator);
        Assert.Single(map.Notes);
        Assert.Equal(new WardleyMapSize(900d, 700d), map.Size);
    }

    [Fact]
    public void Parse_LeavesTheVocabularyEmpty_ForAMapThatUsesNoneOfIt()
    {
        // Act.
        var map = ParseFixture("minimal.owm");

        // Assert. Absent is empty, not null - a caller never has to null-check the vocabulary.
        Assert.Empty(map.Evolves);
        Assert.Empty(map.Attitudes);
        Assert.Empty(map.Accelerators);
        Assert.Empty(map.Notes);
        Assert.Empty(map.Annotations);
        Assert.Empty(map.Urls);
        Assert.Null(map.AnnotationsPosition);
    }
}
