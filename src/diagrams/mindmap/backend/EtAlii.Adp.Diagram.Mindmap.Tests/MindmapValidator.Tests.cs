using Xunit;

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapValidatorTests
{
    private readonly MindmapValidator _validator = new();

    [Fact]
    public void TheValidatorAnswersForTheMindmapOrigin()
    {
        Assert.Equal(Diagram.Definition.Origin, _validator.Origin);
    }

    [Fact]
    public async Task TheGenuineFreeplaneFixture_ValidatesClean()
    {
        // The reference document, saved by Freeplane itself - including its deliberately
        // empty node, which is style, not a problem.
        var document = await File.ReadAllTextAsync("Fixtures/architecture.mm", TestContext.Current.CancellationToken);

        var problems = await Validate(document);

        Assert.Empty(problems);
    }

    [Fact]
    public async Task AMapThatWillNotParse_IsOneErrorNamingTheFile()
    {
        var problems = await Validate("<map version=\"freeplane 1.12.15\"><node TEXT=\"unclosed\"</map>");

        var problem = Assert.Single(problems);
        Assert.Equal("mindmap.not-a-map", problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("architecture", problem.Message);
    }

    [Fact]
    public async Task AFileThatIsNotAMapAtAll_IsTheSameError()
    {
        var problems = await Validate("just some text");

        Assert.Equal("mindmap.not-a-map", Assert.Single(problems).RuleId);
    }

    [Fact]
    public async Task AnUnnamedCentralTopic_IsAWarningOnTheRoot()
    {
        var problems = await Validate("<map version=\"freeplane 1.12.15\"><node TEXT=\"\" ID=\"ID_1\"><node TEXT=\"a child\" ID=\"ID_2\"/></node></map>");

        var problem = Assert.Single(problems);
        Assert.Equal("mindmap.unnamed-root", problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Equal(new DiagramProblemElementLocation("ID_1"), problem.Location);
    }

    [Fact]
    public async Task AnEmptyOrdinaryNode_IsNotAProblem()
    {
        // Freeplane keeps empty nodes; only the central topic must say what the map is about.
        var problems = await Validate("<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\"><node TEXT=\"\" ID=\"ID_2\"/></node></map>");

        Assert.Empty(problems);
    }

    [Fact]
    public async Task TwoNodesSharingAnId_AreAnError()
    {
        var problems = await Validate(
            "<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\">" +
            "<node TEXT=\"first\" ID=\"ID_2\"/><node TEXT=\"second\" ID=\"ID_2\"/></node></map>");

        var problem = Assert.Single(problems);
        Assert.Equal("mindmap.duplicate-id", problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("ID_2", problem.Message);
        Assert.Contains("first", problem.Message);
        Assert.Contains("second", problem.Message);
    }

    [Fact]
    public async Task SeveralRules_CanFireTogether()
    {
        var problems = await Validate(
            "<map version=\"freeplane 1.12.15\"><node TEXT=\"\" ID=\"ID_1\">" +
            "<node TEXT=\"first\" ID=\"ID_2\"/><node TEXT=\"second\" ID=\"ID_2\"/></node></map>");

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.RuleId == "mindmap.unnamed-root");
        Assert.Contains(problems, problem => problem.RuleId == "mindmap.duplicate-id");
    }

    private async Task<IReadOnlyList<DiagramProblem>> Validate(string document) =>
        await _validator.ValidateAsync(document, "architecture", TestContext.Current.CancellationToken);
}
