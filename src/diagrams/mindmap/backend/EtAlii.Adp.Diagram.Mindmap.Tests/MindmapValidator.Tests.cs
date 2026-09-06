using EtAlii.Adp.Common;
using Xunit;
using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.Mindmap.Tests;

public class MindmapValidatorTests
{
    private readonly MindmapValidator _validator = new();

    [Fact]
    public void TheValidatorAnswersForTheMindmapOrigin()
    {
        // Arrange, act and assert.
        Assert.Equal(Diagram.Mindmap.Origin, _validator.Origin);
    }

    [Fact]
    public async Task TheGenuineFreeplaneFixture_ValidatesClean()
    {
        // Arrange.
        // The reference document, saved by Freeplane itself - including its deliberately
        // empty node, which is style, not a problem.
        var document = await File.ReadAllTextAsync("Fixtures/architecture.mm", TestContext.Current.CancellationToken);

        // Act.
        var problems = await Validate(document);

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task AMapThatWillNotParse_IsOneErrorNamingTheFile()
    {
        // Arrange.
        var problems = await Validate("<map version=\"freeplane 1.12.15\"><node TEXT=\"unclosed\"</map>");

        // Act and assert, step by step.
        var problem = Assert.Single(problems);
        Assert.Equal("mindmap.not-a-map", problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Error, problem.Severity);
        Assert.Contains("architecture", problem.Message);
    }

    [Fact]
    public async Task AFileThatIsNotAMapAtAll_IsTheSameError()
    {
        // Act.
        var problems = await Validate("just some text");

        // Assert.
        Assert.Equal("mindmap.not-a-map", Assert.Single(problems).RuleId);
    }

    [Fact]
    public async Task AnUnnamedCentralTopic_IsAWarningOnTheRoot()
    {
        // Arrange.
        var problems = await Validate("<map version=\"freeplane 1.12.15\"><node TEXT=\"\" ID=\"ID_1\"><node TEXT=\"a child\" ID=\"ID_2\"/></node></map>");

        // Act and assert, step by step.
        var problem = Assert.Single(problems);
        Assert.Equal("mindmap.unnamed-root", problem.RuleId);
        Assert.Equal(DiagramProblemSeverity.Warning, problem.Severity);
        Assert.Equal(new DiagramProblemElementLocation("ID_1"), problem.Location);
    }

    [Fact]
    public async Task AnEmptyOrdinaryNode_IsNotAProblem()
    {
        // Act.
        // Freeplane keeps empty nodes; only the central topic must say what the map is about.
        var problems = await Validate("<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\"><node TEXT=\"\" ID=\"ID_2\"/></node></map>");

        // Assert.
        Assert.Empty(problems);
    }

    [Fact]
    public async Task TwoNodesSharingAnId_AreAnError()
    {
        // Arrange.
        var problems = await Validate(
            "<map version=\"freeplane 1.12.15\"><node TEXT=\"root\" ID=\"ID_1\">" +
            "<node TEXT=\"first\" ID=\"ID_2\"/><node TEXT=\"second\" ID=\"ID_2\"/></node></map>");

        // Act and assert, step by step.
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
        // Arrange and act.
        var problems = await Validate(
            "<map version=\"freeplane 1.12.15\"><node TEXT=\"\" ID=\"ID_1\">" +
            "<node TEXT=\"first\" ID=\"ID_2\"/><node TEXT=\"second\" ID=\"ID_2\"/></node></map>");

        // Assert.
        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.RuleId == "mindmap.unnamed-root");
        Assert.Contains(problems, problem => problem.RuleId == "mindmap.duplicate-id");
    }

    // The mindmap rules read the text and nothing else today, so the request's paths are
    // plausible rather than real - no rule resolves one. They stop being decorative the day a
    // broken-link rule is written, which the seam now allows.
    private async Task<IReadOnlyList<DiagramProblem>> Validate(string document) =>
        await _validator.ValidateAsync(
            new DiagramValidationRequest(
                document,
                "architecture",
                RootPath: Root,
                BodyPath: IoPath.Combine(Root, "architecture.mm"),
                RegistrationPath: IoPath.Combine(Root, "architecture.adp")),
            TestContext.Current.CancellationToken);

    private static readonly string Root = IoPath.Combine(IoPath.GetTempPath(), "mindmap-validator-tests");
}
