using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// Templates, which are where a pipeline stops being one file. Two things are being held here: a
/// template that can be followed contributes elements marked as coming from it, and a template that
/// cannot be followed is said so rather than dropped - a diagram that quietly omits part of a
/// pipeline is worse than one that admits the gap (Requirement 5.3).
/// </summary>
public class PipelineTemplatesTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(
        IoPath.GetTempPath(),
        "adp-pipeline-templates-" + Guid.NewGuid().ToString("N"));

    public PipelineTemplatesTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);

        GC.SuppressFinalize(this);
    }

    private string Write(string relativePath, string content)
    {
        var full = IoPath.Combine(_workspace, relativePath.Replace('/', IoPath.DirectorySeparatorChar));
        Directory.CreateDirectory(IoPath.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    private PipelineModel ParseInWorkspace(string relativePath)
    {
        var path = IoPath.Combine(_workspace, relativePath.Replace('/', IoPath.DirectorySeparatorChar));
        var document = PipelineDocument.Parse(File.ReadAllText(path));
        return PipelineParser.Parse(document, new PipelineTemplates(_workspace), path);
    }

    /// <summary>The corpus lives beside the test binary; these copy from it into a real workspace.</summary>
    private void CopyFixture(string name, string relativePath) =>
        Write(relativePath, File.ReadAllText(IoPath.Combine("Fixtures", name.Replace('/', IoPath.DirectorySeparatorChar))));

    [Fact]
    public void AJobsTemplate_ContributesItsJobsToTheStageThatReferencedIt()
    {
        // Arrange.
        CopyFixture("templates.yml", "azure-pipelines.yml");
        CopyFixture("templates/build-jobs.yml", "templates/build-jobs.yml");
        CopyFixture("templates/test-steps.yml", "templates/test-steps.yml");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        var build = model.Stages.Single(stage => stage.Name == "Build");
        var compile = Assert.Single(build.Jobs);
        Assert.Equal("Compile", compile.Name);
        Assert.Equal("Build/Compile", compile.Id);
        Assert.Equal("templates/build-jobs.yml", compile.Template);
        Assert.True(compile.IsFromTemplate);
    }

    [Fact]
    public void AStageThatAuthoredItsOwnJobs_DoesNotMarkThemAsTemplateSourced()
    {
        // Arrange: the distinction Requirement 5.2 turns on - authored-here from included-there.
        CopyFixture("templates.yml", "azure-pipelines.yml");
        CopyFixture("templates/build-jobs.yml", "templates/build-jobs.yml");
        CopyFixture("templates/test-steps.yml", "templates/test-steps.yml");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        var unit = model.Jobs.Single(job => job.Name == "Unit");
        Assert.Equal("", unit.Template);
        Assert.False(unit.IsFromTemplate);
    }

    [Fact]
    public void AStepsTemplate_ContributesItsStepsWhereItWasReferenced()
    {
        // Arrange.
        CopyFixture("templates.yml", "azure-pipelines.yml");
        CopyFixture("templates/build-jobs.yml", "templates/build-jobs.yml");
        CopyFixture("templates/test-steps.yml", "templates/test-steps.yml");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");
        var unit = model.Jobs.Single(job => job.Name == "Unit");

        // Assert.
        // The template stays visible as a step of its own, and what it contributes follows it.
        Assert.Equal(
            [PipelineStepKind.Template, PipelineStepKind.Script],
            unit.Steps.Select(step => step.Kind));
        Assert.Equal("Test from the template", unit.Steps[1].Label);
        Assert.Equal("templates/test-steps.yml", unit.Steps[1].Template);
    }

    [Fact]
    public void AStepsTemplatesContributions_GetTheirOwnPlacesInTheJob()
    {
        // Arrange: ids address deltas, so two steps sharing one would move the wrong element.
        CopyFixture("templates.yml", "azure-pipelines.yml");
        CopyFixture("templates/build-jobs.yml", "templates/build-jobs.yml");
        CopyFixture("templates/test-steps.yml", "templates/test-steps.yml");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        var ids = model.Steps.Select(step => step.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ATemplateInAnotherRepository_IsReportedRatherThanFollowed()
    {
        // Arrange: templates.yml deliberately references `...@shared`, a repository resource that
        // is not checked out here and never will be.
        CopyFixture("templates.yml", "azure-pipelines.yml");
        CopyFixture("templates/build-jobs.yml", "templates/build-jobs.yml");
        CopyFixture("templates/test-steps.yml", "templates/test-steps.yml");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        var unresolved = Assert.Single(model.Unresolved);
        Assert.Equal(PipelineTemplateUnresolvedReason.OtherRepository, unresolved.Reason);
        Assert.Equal("templates/deploy-stages.yml", unresolved.Reference.Path);
        Assert.Contains("shared", unresolved.Explanation);
    }

    [Fact]
    public void ATemplatePathLeavingTheWorkspace_IsRefused()
    {
        // Arrange: a pipeline file is written by whoever opened the project, and this line looks
        // entirely ordinary. Following it would make a diagram a way to read arbitrary files.
        Write("azure-pipelines.yml", "stages:\n  - template: ../outside/steal.yml\n");
        Directory.CreateDirectory(IoPath.Combine(IoPath.GetDirectoryName(_workspace)!, "outside"));
        var outside = IoPath.Combine(IoPath.GetDirectoryName(_workspace)!, "outside", "steal.yml");
        File.WriteAllText(outside, "stages:\n  - stage: Stolen\n");

        try
        {
            // Act.
            var model = ParseInWorkspace("azure-pipelines.yml");

            // Assert.
            Assert.Empty(model.Stages);
            var unresolved = Assert.Single(model.Unresolved);
            Assert.Equal(PipelineTemplateUnresolvedReason.OutsideWorkspace, unresolved.Reason);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    [Fact]
    public void AnAbsoluteWindowsPath_IsRefused()
    {
        // Arrange: the other way out of the workspace, which a `..` check alone would miss.
        Write("azure-pipelines.yml", "stages:\n  - template: C:/Windows/win.ini\n");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        Assert.Equal(PipelineTemplateUnresolvedReason.OutsideWorkspace, Assert.Single(model.Unresolved).Reason);
    }

    [Fact]
    public void ARootedPath_ResolvesAgainstTheWorkspaceRoot()
    {
        // Arrange: Azure reads a leading slash as repository-root-relative, so this one is inside
        // the workspace and must be followed rather than refused.
        Write("nested/azure-pipelines.yml", "stages:\n  - template: /shared/stages.yml\n");
        Write("shared/stages.yml", "stages:\n  - stage: FromRoot\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");

        // Act.
        var model = ParseInWorkspace("nested/azure-pipelines.yml");

        // Assert.
        Assert.Empty(model.Unresolved);
        Assert.Equal("FromRoot", Assert.Single(model.Stages).Name);
    }

    [Fact]
    public void ATemplatePathBuiltFromAParameter_IsReportedRatherThanGuessedAt()
    {
        // Arrange.
        Write("azure-pipelines.yml", "stages:\n  - template: templates/${{ parameters.which }}.yml\n");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        var unresolved = Assert.Single(model.Unresolved);
        Assert.Equal(PipelineTemplateUnresolvedReason.ParameterDependent, unresolved.Reason);
        Assert.Contains("parameter", unresolved.Explanation);
    }

    [Fact]
    public void AMissingTemplate_IsReportedAsMissing()
    {
        // Arrange.
        Write("azure-pipelines.yml", "stages:\n  - template: templates/gone.yml\n");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        Assert.Equal(PipelineTemplateUnresolvedReason.NotFound, Assert.Single(model.Unresolved).Reason);
    }

    [Fact]
    public void ATemplateThatIncludesItself_IsStoppedRatherThanFollowedForever()
    {
        // Arrange.
        Write("azure-pipelines.yml", "stages:\n  - template: templates/loop.yml\n");
        Write("templates/loop.yml", "stages:\n  - template: loop.yml\n");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        Assert.Contains(model.Unresolved, unresolved => unresolved.Reason == PipelineTemplateUnresolvedReason.Cyclic);
    }

    [Fact]
    public void AnUnparseableTemplate_IsReportedWithoutTakingTheDiagramDown()
    {
        // Arrange: one broken template is a gap in the diagram, not the end of it.
        Write("azure-pipelines.yml", "stages:\n  - stage: Build\n    jobs:\n      - template: templates/broken.yml\n");
        Write("templates/broken.yml", "jobs:\n  - job: A\n   steps:\n      - script: x\n     bad: [indent\n");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        Assert.Equal("Build", Assert.Single(model.Stages).Name);
        Assert.Equal(PipelineTemplateUnresolvedReason.Unreadable, Assert.Single(model.Unresolved).Reason);
    }

    [Fact]
    public void AnExtendingFile_TakesItsShapeFromTheTemplate()
    {
        // Arrange: Requirement 5.5 - the one case where a diagram of a file is mostly not about
        // that file, so everything it shows must be marked as living elsewhere.
        CopyFixture("extends.yml", "azure-pipelines.yml");
        CopyFixture("templates/pipeline.yml", "templates/pipeline.yml");

        // Act.
        var model = ParseInWorkspace("azure-pipelines.yml");

        // Assert.
        Assert.NotNull(model.Extends);
        Assert.Equal(["buildConfiguration", "deployTo"], model.Extends.ParameterNames);
        var stage = Assert.Single(model.Stages);
        Assert.Equal("Build", stage.Name);
        Assert.Equal("templates/pipeline.yml", stage.Template);
        Assert.All(model.Steps, step => Assert.True(step.IsFromTemplate));
    }

    [Fact]
    public void ATemplateSharedByTwoPipelines_IsReadOnce()
    {
        // Arrange: the cache is what keeps a template shared by twenty pipelines from being read
        // twenty times. Proven by rewriting the file between the two parses: a second read from
        // disk would see the new content, a second read from the cache cannot.
        Write("one.yml", "jobs:\n  - template: templates/shared.yml\n");
        Write("two.yml", "jobs:\n  - template: templates/shared.yml\n");
        var shared = Write("templates/shared.yml", "jobs:\n  - job: Before\n    steps:\n      - script: x\n");
        var resolver = new PipelineTemplates(_workspace);
        var one = IoPath.Combine(_workspace, "one.yml");
        var two = IoPath.Combine(_workspace, "two.yml");

        // Act.
        var first = PipelineParser.Parse(PipelineDocument.Parse(File.ReadAllText(one)), resolver, one);
        File.WriteAllText(shared, "jobs:\n  - job: After\n    steps:\n      - script: x\n");
        var second = PipelineParser.Parse(PipelineDocument.Parse(File.ReadAllText(two)), resolver, two);

        // Assert.
        // The staleness is the point, and is why the document store calls Forget when a file
        // changes underneath it rather than hoping the cache notices.
        Assert.Equal("Before", first.Jobs.Single().Name);
        Assert.Equal("Before", second.Jobs.Single().Name);
        Assert.Empty(second.Unresolved);
    }

    [Fact]
    public void ATemplateDeletedFromDisk_IsReportedMissingRatherThanServedFromTheCache()
    {
        // Arrange: the cache holds contents, not the fact that a file exists - so a template that
        // has gone is reported gone, even to a resolver that read it a moment ago.
        Write("one.yml", "jobs:\n  - template: templates/shared.yml\n");
        var shared = Write("templates/shared.yml", "jobs:\n  - job: Shared\n    steps:\n      - script: x\n");
        var resolver = new PipelineTemplates(_workspace);
        var one = IoPath.Combine(_workspace, "one.yml");
        PipelineParser.Parse(PipelineDocument.Parse(File.ReadAllText(one)), resolver, one);

        // Act.
        File.Delete(shared);
        var model = PipelineParser.Parse(PipelineDocument.Parse(File.ReadAllText(one)), resolver, one);

        // Assert.
        Assert.Equal(PipelineTemplateUnresolvedReason.NotFound, Assert.Single(model.Unresolved).Reason);
    }

    [Fact]
    public void ForgettingWhatWasRead_MakesTheNextReadGoBackToDisk()
    {
        // Arrange: a file changed on disk must be picked up, which is what the store calls this for.
        Write("one.yml", "jobs:\n  - template: templates/shared.yml\n");
        var shared = Write("templates/shared.yml", "jobs:\n  - job: Before\n    steps:\n      - script: x\n");
        var resolver = new PipelineTemplates(_workspace);
        var path = IoPath.Combine(_workspace, "one.yml");
        PipelineParser.Parse(PipelineDocument.Parse(File.ReadAllText(path)), resolver, path);

        // Act.
        File.WriteAllText(shared, "jobs:\n  - job: After\n    steps:\n      - script: x\n");
        resolver.Forget();
        var model = PipelineParser.Parse(PipelineDocument.Parse(File.ReadAllText(path)), resolver, path);

        // Assert.
        Assert.Equal("After", model.Jobs.Single().Name);
    }

    [Fact]
    public void WithoutAResolver_ATemplateIsNeitherFollowedNorReportedAsUnresolved()
    {
        // Arrange: the parse-only overload makes no attempt, which is different from trying and
        // failing - so it must not put a template in the unresolved list and claim it did.
        var document = PipelineDocument.Parse("stages:\n  - template: templates/anything.yml\n");

        // Act.
        var model = PipelineParser.Parse(document);

        // Assert.
        Assert.Single(model.Templates);
        Assert.Empty(model.Unresolved);
    }
}
