using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzurePipeline.Tests;

/// <summary>
/// The writer is the only thing in this module that changes a file, and the file is a build
/// definition somebody's releases depend on. So almost every test here checks two things: that the
/// intended change happened, and that every other byte of the file is exactly as it was.
/// </summary>
public class PipelineWriterTests
{
    private static PipelineDocument FixtureDocument(string name) =>
        PipelineDocument.Parse(File.ReadAllText(IoPath.Combine("Fixtures", name)));

    /// <summary>The lines of a document, for comparing what an edit did and did not touch.</summary>
    private static string[] Snapshot(PipelineDocument document) =>
        document.Lines.Select(line => line.ToString()).ToArray();

    /// <summary>
    /// Asserts that the only lines differing between two snapshots are the ones expected to.
    /// </summary>
    private static void AssertOnlyTheseLinesChanged(string[] before, string[] after, int start, int removed, int added)
    {
        Assert.Equal(before.Length - removed + added, after.Length);
        for (var index = 0; index < start; index++)
        {
            Assert.Equal(before[index], after[index]);
        }

        for (var offset = 0; offset < before.Length - start - removed; offset++)
        {
            Assert.Equal(before[start + removed + offset], after[start + added + offset]);
        }
    }

    private static PipelineStage StageIn(PipelineDocument document, string name) =>
        PipelineParser.Parse(document).Stages.Single(stage => stage.Name == name);

    [Fact]
    public void ARename_ChangesTheOneLineHoldingTheName()
    {
        // Arrange.
        var document = FixtureDocument("multi-stage.yml");
        var build = StageIn(document, "Build");
        var before = Snapshot(document);

        // Act.
        var changed = new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "Build everything");

        // Assert.
        var after = Snapshot(document);
        Assert.True(changed);
        var line = Array.FindIndex(before, text => text.Contains("displayName: Build the solution", StringComparison.Ordinal));
        Assert.Contains("displayName: Build everything", after[line]);
        AssertOnlyTheseLinesChanged(before, after, line, removed: 1, added: 1);
    }

    [Fact]
    public void ARename_KeepsTheIndentationTheFileWasUsing()
    {
        // Arrange: an unindented pipeline is valid YAML that a reformatter would silently "fix",
        // so an edit has to match what it finds rather than what it would have written.
        var document = FixtureDocument("edge-indentation.yml");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "Built");

        // Assert.
        Assert.Contains("\n  displayName: Built\n", document.Text);
    }

    [Fact]
    public void ARename_OfAStage_DoesNotFindTheNameOfOneOfItsJobs()
    {
        // Arrange: a stage's range contains its jobs, and a job has a displayName too. Editing the
        // wrong one would rename something the user was not looking at.
        var document = FixtureDocument("multi-stage.yml");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "Renamed");

        // Assert.
        Assert.Contains("displayName: Renamed", document.Text);
        Assert.Contains("displayName: Compile", document.Text);
    }

    [Fact]
    public void ARenameThatChangesNothing_WritesNothing()
    {
        // Arrange: a no-op edit must not put a line in somebody's diff.
        var document = FixtureDocument("multi-stage.yml");
        var build = StageIn(document, "Build");
        var before = Snapshot(document);

        // Act.
        var changed = new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "Build the solution");

        // Assert.
        Assert.False(changed);
        Assert.Equal(before, Snapshot(document));
    }

    [Fact]
    public void ARename_KeepsATrailingCommentOnTheLineItRewrites()
    {
        // Arrange: the comment is about that property. Losing it while renaming something is an
        // edit nobody asked for.
        var document = PipelineDocument.Parse(
            "stages:\n  - stage: Build\n    displayName: Old   # why it is called that\n");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "New");

        // Assert.
        Assert.Contains("displayName: New   # why it is called that", document.Text);
    }

    [Fact]
    public void AHashInsideAQuotedValue_IsNotMistakenForAComment()
    {
        // Arrange: a `#` only starts a comment after whitespace and outside quotes; treating this
        // one as a comment would truncate the name it is part of.
        var document = PipelineDocument.Parse(
            "stages:\n  - stage: Build\n    displayName: \"Build #2\"\n");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "New");

        // Assert.
        Assert.Contains("displayName: New\n", document.Text);
        Assert.DoesNotContain("#2", document.Text);
    }

    [Fact]
    public void AnElementWithoutADisplayName_GainsOneWithoutDisturbingTheRest()
    {
        // Arrange.
        var document = FixtureDocument("multi-stage.yml");
        var staging = StageIn(document, "DeployStaging");
        var before = Snapshot(document);

        // Act.
        var changed = new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(staging), "Deploy to staging");

        // Assert.
        var after = Snapshot(document);
        Assert.True(changed);
        AssertOnlyTheseLinesChanged(before, after, staging.Lines.Start + 1, removed: 0, added: 1);
        Assert.Equal("    displayName: Deploy to staging\n", after[staging.Lines.Start + 1]);
    }

    [Fact]
    public void RemovingADisplayName_TakesItsLineAndNoOther()
    {
        // Arrange: an element without one falls back to its own name, so removing it is a real
        // instruction rather than the same as setting it to nothing.
        var document = FixtureDocument("multi-stage.yml");
        var build = StageIn(document, "Build");
        var before = Snapshot(document);
        var line = Array.FindIndex(before, text => text.Contains("displayName: Build the solution", StringComparison.Ordinal));

        // Act.
        var changed = new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "");

        // Assert.
        Assert.True(changed);
        AssertOnlyTheseLinesChanged(before, Snapshot(document), line, removed: 1, added: 0);
    }

    [Fact]
    public void AStageGainingADependsOnItNeverHad_InsertsLinesWithoutDisturbingComments()
    {
        // Arrange: Requirement 9.3 - making the implicit explicit. The Build stage relies on the
        // sequential default today; writing it down must not reflow anything around it.
        var document = FixtureDocument("multi-stage.yml");
        var build = StageIn(document, "Build");
        var before = Snapshot(document);

        // Act.
        var changed = new PipelineWriter(document).SetDependsOn(PipelineEditTarget.For(build), ["Prepare"]);

        // Assert.
        var after = Snapshot(document);
        Assert.True(changed);
        AssertOnlyTheseLinesChanged(before, after, build.Lines.Start + 1, removed: 0, added: 1);
        Assert.Equal("    dependsOn: Prepare\n", after[build.Lines.Start + 1]);
    }

    [Fact]
    public void ADependsOnListRewrite_LeavesTheCommentAboveItAlone()
    {
        // Arrange: multi-stage.yml puts an explanatory comment directly above Notify's dependsOn.
        // A property block that swallowed the line above it would delete that comment.
        var document = FixtureDocument("multi-stage.yml");
        var notify = StageIn(document, "Notify");
        var before = Snapshot(document);

        // Act.
        new PipelineWriter(document).SetDependsOn(PipelineEditTarget.For(notify), ["DeployStaging"]);

        // Assert.
        var after = Snapshot(document);
        Assert.Contains(after, text => text.Contains("Fan-in: waits for both deployments.", StringComparison.Ordinal));
        Assert.Equal(before.Length - 3 + 1, after.Length);
    }

    [Fact]
    public void ADependsOnOfSeveralNames_IsWrittenAsABlockList()
    {
        // Arrange.
        var document = FixtureDocument("multi-stage.yml");
        var test = StageIn(document, "Test");

        // Act.
        new PipelineWriter(document).SetDependsOn(PipelineEditTarget.For(test), ["Build", "Prepare"]);

        // Assert.
        Assert.Contains("    dependsOn:\n      - Build\n      - Prepare\n", document.Text);
    }

    [Fact]
    public void ADependsOnOfNothing_IsWrittenAsAnEmptyListRatherThanRemoved()
    {
        // Arrange: `dependsOn: []` means "wait for nothing", which is the opposite of what a stage
        // with no dependsOn at all does. Removing the key would say the other thing.
        var document = FixtureDocument("multi-stage.yml");
        var test = StageIn(document, "Test");

        // Act.
        new PipelineWriter(document).SetDependsOn(PipelineEditTarget.For(test), []);

        // Assert.
        Assert.Contains("    dependsOn: []\n", document.Text);
        Assert.Equal([], PipelineParser.Parse(document).Stages.Single(stage => stage.Name == "Test").DependsOn);
        Assert.True(PipelineParser.Parse(document).Stages.Single(stage => stage.Name == "Test").DependsOnDeclared);
    }

    [Fact]
    public void ClearingADependsOn_PutsTheElementBackOnTheDefault()
    {
        // Arrange.
        var document = FixtureDocument("multi-stage.yml");
        var test = StageIn(document, "Test");

        // Act.
        var changed = new PipelineWriter(document).ClearDependsOn(PipelineEditTarget.For(test));

        // Assert.
        Assert.True(changed);
        Assert.False(PipelineParser.Parse(document).Stages.Single(stage => stage.Name == "Test").DependsOnDeclared);
    }

    [Fact]
    public void AnEditedFile_IsOtherwiseByteForByteWhatItWas()
    {
        // Arrange: the guarantee the whole module is built around, checked end to end rather than
        // line by line - comments, blank lines, anchors and quoting all still there afterwards.
        var original = File.ReadAllText(IoPath.Combine("Fixtures", "edge-comments.yml"));
        var document = PipelineDocument.Parse(original);
        var stage = PipelineParser.Parse(document).Stages[0];

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(stage), "Renamed");

        // Assert.
        // The stage here has no displayName, so the edit inserts one line. Every other line of the
        // file is untouched - the comments, the blank lines, and the block scalar with a hash in it
        // that a serialiser would have been entitled to reformat.
        var before = PipelineDocument.Parse(original).Lines.Select(line => line.ToString()).ToArray();
        var after = document.Lines.Select(line => line.ToString()).ToArray();
        var inserted = stage.Lines.Start + 1;
        Assert.Equal("    displayName: Renamed\n", after[inserted]);
        AssertOnlyTheseLinesChanged(before, after, inserted, removed: 0, added: 1);
    }

    [Fact]
    public void AnEditToAnAnchoredFile_LeavesTheAnchorsAsWritten()
    {
        // Arrange: a serialiser would expand `<<: *defaults` and lose the author's intent. A
        // line-scoped edit cannot, because it never goes near those lines.
        var original = File.ReadAllText(IoPath.Combine("Fixtures", "edge-anchors.yml"));
        var document = PipelineDocument.Parse(original);
        var two = StageIn(document, "Two");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(two), "The second one");

        // Assert.
        Assert.Contains(".defaults: &defaults", document.Text);
        Assert.Equal(2, document.Text.Split("<<: *defaults").Length - 1);
    }

    [Fact]
    public void AnEditToACrlfFile_DoesNotIntroduceALoneNewline()
    {
        // Arrange: an inserted line adopts the file's own terminator, so an edit does not leave a
        // mixed-ending file behind for the next reader to wonder about.
        // This file is steps-only, so the only thing in it that is actually written down - and so
        // the only thing editable - is the step.
        var document = FixtureDocument("edge-crlf.yml");
        var step = PipelineParser.Parse(document).Steps.First();

        // Act.
        new PipelineWriter(document).SetEnabled(PipelineEditTarget.For(step), "false");

        // Assert.
        Assert.All(
            document.Lines.Take(document.Lines.Count - 1),
            line => Assert.Equal("\r\n", line.Ending));
    }

    [Fact]
    public void AValueThatWouldChangeMeaningUnquoted_IsQuoted()
    {
        // Arrange: a colon-space inside a bare scalar makes it a mapping, which would turn a
        // rename into a syntax error a build only finds later.
        var document = PipelineDocument.Parse("stages:\n  - stage: Build\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "Build: everything");

        // Assert.
        Assert.Contains("displayName: \"Build: everything\"", document.Text);
        Assert.Equal("Build: everything", StageIn(document, "Build").DisplayName);
    }

    [Fact]
    public void AnOrdinaryValue_IsNotQuoted()
    {
        // Arrange: a quote appearing around a name that never had one is a diff line a reviewer
        // has to stop and think about.
        var document = PipelineDocument.Parse("stages:\n  - stage: Build\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "Build the solution");

        // Assert.
        Assert.Contains("displayName: Build the solution\n", document.Text);
    }

    [Fact]
    public void AnExpressionValue_IsWrittenExactlyAsGiven()
    {
        // Arrange: quoting an expression would still be valid YAML and would stop it being an
        // expression, which is the one thing this module promises never to do to one.
        var document = PipelineDocument.Parse("stages:\n  - stage: Build\n    jobs:\n      - job: A\n        steps:\n          - script: x\n");
        var build = StageIn(document, "Build");

        // Act.
        new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(build), "${{ parameters.title }}");

        // Assert.
        Assert.Contains("displayName: ${{ parameters.title }}\n", document.Text);
    }

    [Fact]
    public void AnEditToAJob_StaysInsideThatJob()
    {
        // Arrange: two jobs in one stage, one of which already has the property being set.
        var document = FixtureDocument("multi-stage.yml");
        var model = PipelineParser.Parse(document);
        var integration = model.Jobs.Single(job => job.Name == "Integration");
        var before = Snapshot(document);

        // Act.
        new PipelineWriter(document).SetEnabled(PipelineEditTarget.For(integration), "false");

        // Assert.
        var after = Snapshot(document);
        AssertOnlyTheseLinesChanged(before, after, integration.Lines.Start + 1, removed: 0, added: 1);
        Assert.False(PipelineParser.Parse(document).Jobs.Single(job => job.Name == "Unit").Execution.IsDisabled);
        Assert.True(PipelineParser.Parse(document).Jobs.Single(job => job.Name == "Integration").Execution.IsDisabled);
    }

    [Fact]
    public void AnEditToAStep_StaysInsideThatStep()
    {
        // Arrange: steps are the narrowest ranges in the model, and the easiest to overrun.
        var document = FixtureDocument("multi-stage.yml");
        var compile = PipelineParser.Parse(document).Jobs.Single(job => job.Name == "Compile");
        var before = Snapshot(document);

        // Act.
        new PipelineWriter(document).SetEnabled(PipelineEditTarget.For(compile.Steps[1]), "false");

        // Assert.
        var after = Snapshot(document);
        AssertOnlyTheseLinesChanged(before, after, compile.Steps[1].Lines.Start + 1, removed: 0, added: 1);
        var steps = PipelineParser.Parse(document).Jobs.Single(job => job.Name == "Compile").Steps;
        Assert.Equal([false, true, false], steps.Select(step => step.Execution.IsDisabled));
    }

    [Fact]
    public void AnEditOutsideTheDocument_Throws()
    {
        // Arrange: an off-by-one in a range rewrites somebody's pipeline, so it is a programming
        // error rather than something to absorb.
        var document = PipelineDocument.Parse("stages:\n  - stage: Build\n");

        // Act & assert.
        var target = new PipelineEditTarget("x", new PipelineLineRange(0, 40), IsImplicit: false, "");
        Assert.Throws<ArgumentOutOfRangeException>(() => new PipelineWriter(document).SetDisplayName(target, "x"));
    }

    [Fact]
    public void AnImplicitStage_IsRefusedRatherThanEditedSomewhereElse()
    {
        // Arrange: a jobs-only file has a stage the schema conjured, whose range covers lines that
        // belong to its jobs. Writing a displayName "on the stage" would land on the first job -
        // an edit to something the user was not looking at, in a file they cannot easily unpick.
        var document = FixtureDocument("jobs-only.yml");
        var stage = PipelineParser.Parse(document).Stages.Single();
        var before = Snapshot(document);

        // Act & assert.
        Assert.True(stage.IsImplicit);
        var error = Assert.Throws<InvalidOperationException>(
            () => new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(stage), "Everything"));
        Assert.Contains("nothing here to edit", error.Message);
        Assert.Equal(before, Snapshot(document));
    }

    [Fact]
    public void AnElementFromATemplate_IsRefusedAndSaysWhereToEditItInstead()
    {
        // Arrange: its text is in another file, so an edit here would land in the wrong one
        // (Requirement 5.4).
        var target = new PipelineEditTarget("Build/Compile", new PipelineLineRange(0, 1), IsImplicit: false, "templates/build-jobs.yml");
        var document = PipelineDocument.Parse("jobs:\n  - job: Compile\n");

        // Act & assert.
        var error = Assert.Throws<InvalidOperationException>(
            () => new PipelineWriter(document).SetDisplayName(target, "Renamed"));
        Assert.Contains("templates/build-jobs.yml", error.Message);
    }

    [Fact]
    public void EveryEditableStageInTheCorpus_StillParsesAfterARename()
    {
        // Arrange: a rename that produced a file the parser can no longer read would be the worst
        // outcome available, so this runs one over every stage the corpus actually declares.
        foreach (var path in Directory.GetFiles("Fixtures", "*.yml", SearchOption.AllDirectories))
        {
            var document = PipelineDocument.Parse(File.ReadAllText(path));
            var stage = PipelineParser.Parse(document).Stages
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(pair => PipelineEditTarget.For(pair.candidate).IsEditable);
            if (stage.candidate is null)
            {
                continue;
            }

            // Act.
            new PipelineWriter(document).SetDisplayName(PipelineEditTarget.For(stage.candidate), "Edited by a test");

            // Assert.
            var reparsed = PipelineParser.Parse(document);
            Assert.Equal("Edited by a test", reparsed.Stages[stage.index].DisplayName);
        }
    }
}
