using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.WardleyMap.Tests;

public class WardleyWriterTests
{
    private static string FixturesPath => IoPath.Combine(AppContext.BaseDirectory, "Fixtures");

    private static string ReadFixture(string name) => File.ReadAllText(IoPath.Combine(FixturesPath, name));

    /// <summary>Parses text into the pair every edit needs: the document to splice, and the model that says where.</summary>
    private static (WardleyDocument Document, WardleyMap Map) Open(string text)
    {
        var document = WardleyDocument.Parse(text);
        return (document, WardleyParser.Parse(document));
    }

    /// <summary>The lines that differ between two texts, as 1-based numbers.</summary>
    private static IReadOnlyList<int> ChangedLines(string before, string after)
    {
        var left = WardleyDocument.Parse(before).Lines;
        var right = WardleyDocument.Parse(after).Lines;
        var changed = new List<int>();
        for (var index = 0; index < Math.Max(left.Count, right.Count); index++)
        {
            var a = index < left.Count ? left[index] : null;
            var b = index < right.Count ? right[index] : null;
            if (a != b)
            {
                changed.Add(index + 1);
            }
        }

        return changed;
    }

    [Fact]
    public void SetPosition_ChangesExactlyOneLine_InAMapFullOfComments()
    {
        // Arrange. A comment-heavy map is where a regenerating writer does its damage.
        var before = ReadFixture("comments-everywhere.owm");
        (WardleyDocument document, WardleyMap map) = Open(before);
        var component = map.Components.Single(candidate => candidate.Name == "Cup of Tea");

        // Act.
        var moved = WardleyWriter.SetPosition(document, component, new WardleyCoordinate(0.55d, 0.42d));

        // Assert. Requirement 3.2 - the whole of it.
        Assert.True(moved);
        Assert.Equal([(int)component.Line], ChangedLines(before, document.ToText()));
    }

    [Fact]
    public void SetPosition_KeepsTheTrailingCommentOnTheLineItEdits()
    {
        // Arrange.
        (WardleyDocument document, WardleyMap map) = Open("component Cup of Tea [0.79, 0.61] // why it sits here\n");
        var component = map.Components.Single();

        // Act.
        WardleyWriter.SetPosition(document, component, new WardleyCoordinate(0.55d, 0.42d));

        // Assert. The comment is part of the author's line, not ours to drop.
        Assert.Equal("component Cup of Tea [0.55, 0.42] // why it sits here\n", document.ToText());
    }

    [Fact]
    public void SetPosition_KeepsDecoratorsAndLabelOffsets()
    {
        // Arrange. None of these survive a line rebuilt from the model, which is why the writer
        // splices a span instead.
        (WardleyDocument document, WardleyMap map) = Open("component Payment [0.70, 0.72] (buy) inertia label [-27, 20]\n");

        // Act.
        WardleyWriter.SetPosition(document, map.Components.Single(), new WardleyCoordinate(0.5d, 0.5d));

        // Assert.
        Assert.Equal("component Payment [0.5, 0.5] (buy) inertia label [-27, 20]\n", document.ToText());
    }

    [Fact]
    public void SetPosition_KeepsTheAuthorsSpacingBetweenTheTwoNumbers()
    {
        // Arrange. The separator is the author's choice; only the numbers are ours.
        (WardleyDocument document, WardleyMap map) = Open("component Alpha [0.80,0.40]\n");

        // Act.
        WardleyWriter.SetPosition(document, map.Components.Single(), new WardleyCoordinate(0.1d, 0.2d));

        // Assert.
        Assert.Equal("component Alpha [0.1,0.2]\n", document.ToText());
    }

    [Fact]
    public void SetPosition_KeepsTheDocumentsLineEndings()
    {
        // Arrange.
        var before = ReadFixture("crlf-line-endings.owm");
        (WardleyDocument document, WardleyMap map) = Open(before);

        // Act.
        WardleyWriter.SetPosition(
            document,
            map.Components.First(),
            new WardleyCoordinate(0.5d, 0.5d));

        // Assert. An edit on a CRLF file must not smuggle LF into it.
        var after = document.ToText();
        Assert.Contains("\r\n", after, StringComparison.Ordinal);
        Assert.Single(ChangedLines(before, after));
    }

    [Fact]
    public void SetPosition_WritesNumbersWithoutATrailingZero()
    {
        // Arrange.
        (WardleyDocument document, WardleyMap map) = Open("component Alpha [0.80, 0.40]\n");

        // Act.
        WardleyWriter.SetPosition(document, map.Components.Single(), new WardleyCoordinate(1d, 0d));

        // Assert. `1` rather than `1.0` - a hand-written file should not gain noise in its diff.
        Assert.Equal("component Alpha [1, 0]\n", document.ToText());
    }

    [Fact]
    public void SetPipelineChildMaturity_ChangesTheChildsSingleNumber()
    {
        // Arrange.
        var before = ReadFixture("pipelines-both-forms.owm");
        (WardleyDocument document, WardleyMap map) = Open(before);
        var child = map.Pipelines
            .SelectMany(pipeline => pipeline.Children)
            .Single(candidate => candidate.Name == "Electric Kettle");

        // Act.
        var moved = WardleyWriter.SetPipelineChildMaturity(document, child, 0.9d);

        // Assert. Requirement 7.4 - a child's visibility is the parent's, so only one number moves.
        Assert.True(moved);
        Assert.Equal([(int)child.Line], ChangedLines(before, document.ToText()));
        Assert.Contains("component Electric Kettle [0.9]", document.ToText(), StringComparison.Ordinal);
    }

    [Fact]
    public void SetPipelineExtent_RewritesTheLegacyFormsOwnCoordinates()
    {
        // Arrange.
        (WardleyDocument document, WardleyMap map) = Open("pipeline Power [0.30, 0.85]\n");
        var pipeline = map.Pipelines.Single();

        // Act.
        var moved = WardleyWriter.SetPipelineExtent(document, pipeline, new WardleyCoordinate(0.2d, 0.9d));

        // Assert. Written back in the form it was read (Requirement 5.4).
        Assert.True(moved);
        Assert.Equal("pipeline Power [0.2, 0.9]\n", document.ToText());
    }

    [Fact]
    public void SetPipelineExtent_RefusesTheNestedForm_WhichHasNoCoordinatesOfItsOwn()
    {
        // Arrange.
        (WardleyDocument document, WardleyMap map) = Open("component Kettle [0.4, 0.4]\npipeline Kettle\n{\n}\n");
        var before = document.ToText();

        // Act.
        var moved = WardleyWriter.SetPipelineExtent(
            document,
            map.Pipelines.Single(),
            new WardleyCoordinate(0.2d, 0.9d));

        // Assert.
        Assert.False(moved);
        Assert.Equal(before, document.ToText());
    }

    [Fact]
    public void Rename_RewritesTheDeclarationAndEveryReference_InOnePass()
    {
        // Arrange.
        const string text = """
            component Kettle [0.43, 0.35]
            component Power [0.10, 0.70]
            Cup of Tea->Kettle
            Kettle->Power
            evolve Kettle 0.62
            pipeline Kettle
            {
              component Electric Kettle [0.63]
            }
            """;
        (WardleyDocument document, WardleyMap map) = Open(text);

        // Act.
        var changed = WardleyWriter.Rename(document, map, "Kettle", "Boiler");

        // Assert. Requirement 4.4 - declaration, both links, the evolve and the pipeline parent.
        Assert.Equal(5, changed);
        var after = document.ToText();
        Assert.Contains("component Boiler [0.43, 0.35]", after, StringComparison.Ordinal);
        Assert.Contains("Cup of Tea->Boiler", after, StringComparison.Ordinal);
        Assert.Contains("Boiler->Power", after, StringComparison.Ordinal);
        Assert.Contains("evolve Boiler 0.62", after, StringComparison.Ordinal);
        Assert.Contains("pipeline Boiler", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_DoesNotTouchANameThatMerelyContainsTheOldOne()
    {
        // Arrange. In this notation a name containing another name is the norm, not an edge
        // case - a naive string replace would corrupt "Cup of Tea" while renaming "Tea".
        const string text = """
            component Tea [0.63, 0.81]
            component Cup of Tea [0.79, 0.61]
            Cup of Tea->Tea
            """;
        (WardleyDocument document, WardleyMap map) = Open(text);

        // Act.
        WardleyWriter.Rename(document, map, "Tea", "Leaves");

        // Assert.
        var after = document.ToText();
        Assert.Contains("component Leaves [0.63, 0.81]", after, StringComparison.Ordinal);
        Assert.Contains("component Cup of Tea [0.79, 0.61]", after, StringComparison.Ordinal);
        Assert.Contains("Cup of Tea->Leaves", after, StringComparison.Ordinal);
    }

    [Fact]
    public void Rename_KeepsALinksContextText()
    {
        // Arrange.
        (WardleyDocument document, WardleyMap map) = Open("Order Portal->Partner Plugins; via the plugin SDK\n");

        // Act.
        WardleyWriter.Rename(document, map, "Partner Plugins", "Marketplace");

        // Assert.
        Assert.Equal("Order Portal->Marketplace; via the plugin SDK\n", document.ToText());
    }

    [Fact]
    public void Rename_HandlesBothEndsOfALinkToItself()
    {
        // Arrange. Legal, and the case where replacing one end could shift the other's span.
        (WardleyDocument document, WardleyMap map) = Open("Alpha->Alpha\n");

        // Act.
        WardleyWriter.Rename(document, map, "Alpha", "Beta");

        // Assert.
        Assert.Equal("Beta->Beta\n", document.ToText());
    }

    [Fact]
    public void Rename_LeavesTheDocumentAloneWhenTheNameIsUnchanged()
    {
        // Arrange.
        var before = ReadFixture("tea-shop.owm");
        (WardleyDocument document, WardleyMap map) = Open(before);

        // Act.
        var changed = WardleyWriter.Rename(document, map, "Kettle", "Kettle");

        // Assert.
        Assert.Equal(0, changed);
        Assert.Equal(before, document.ToText());
    }

    [Fact]
    public void Rename_TouchesNoLineThatDoesNotMentionTheName()
    {
        // Arrange.
        var before = ReadFixture("tea-shop.owm");
        (WardleyDocument document, WardleyMap map) = Open(before);

        // Act.
        WardleyWriter.Rename(document, map, "Kettle", "Boiler");

        // Assert. Three lines mention Kettle in that fixture: its declaration and two links.
        var changed = ChangedLines(before, document.ToText());
        Assert.Equal(3, changed.Count);
    }

    [Fact]
    public void Rename_KeepsTheEvolveRenameTarget()
    {
        // Arrange. `evolve Name->NewName x` - renaming the component must not disturb the name
        // it takes on arrival.
        (WardleyDocument document, WardleyMap map) = Open("evolve Datacentre->Cloud Hosting 0.83\n");

        // Act.
        WardleyWriter.Rename(document, map, "Datacentre", "Server Room");

        // Assert.
        Assert.Equal("evolve Server Room->Cloud Hosting 0.83\n", document.ToText());
    }

    [Fact]
    public void EveryFixtureRoundTripsUnchanged_WhenNothingIsEdited()
    {
        // Arrange. The floor: a corpus that is not found round-trips nothing and passes, which
        // is indistinguishable from every fixture being byte-identical.
        var fixtures = Directory.EnumerateFiles(FixturesPath, "*.owm").ToArray();
        Assert.True(
            fixtures.Length >= 5,
            $"Only {fixtures.Length} .owm fixtures were discovered; this guard has stopped finding the corpus it sweeps.");

        // Act and assert. The writer is only ever called on lines an edit touches, so
        // opening and closing a map without editing must still be byte-identical - the property
        // task 4 established, re-checked with the writer in the picture.
        foreach (var path in fixtures)
        {
            var text = File.ReadAllText(path);
            (WardleyDocument document, _) = Open(text);
            Assert.Equal(text, document.ToText());
        }
    }
}
