using System.Text;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;
using EtAlii.Adp.Documents;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// One pipeline body opened through the module's binding (azure-devops-pipeline-disl-fbl task 8): its bytes as they
/// are, the plugin's reading, the parser's model, the DISL model built from the reading, and what the store keeps.
/// </summary>
public class PipelineBodyTests
{
    public static TheoryData<string> Documents() => [.. PipelineCorpus.Documents().Select(document => document.Relative)];

    [Fact]
    public void ABody_IsItsBytesAsTheyAre()
    {
        // Arrange: CRLF, a BOM-less UTF-8 character and no final newline.
        var bytes = "steps:\r\n  - script: echo café"u8.ToArray();

        // Act.
        var body = PipelineBody.Open(bytes, PipelineReadContext.Unbound);

        // Assert.
        Assert.Same(bytes, body.Bytes);
        Assert.Equal("steps:\r\n  - script: echo café", body.Text);
        Assert.Equal(body.Text, PipelineBody.Parse(body.Text).Text);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void WhatTheStoreKeeps_IsWhatTheParserRead(string path)
    {
        // Arrange.
        var document = PipelineCorpus.Documents().Single(candidate => candidate.Relative == path);
        var full = IoPath.Combine(PipelineCorpus.SourceFolder, document.Relative);
        var root = IoPath.Combine(PipelineCorpus.SourceFolder, document.Root);
        var text = File.ReadAllText(full);
        var expected = PipelineParser.Outcome(LineDocument.Parse(text), new PipelineTemplates(root), full);

        // Act.
        var outcome = PipelineBody.Open(Encoding.UTF8.GetBytes(text), new PipelineReadContext(full, new PipelineTemplates(root))).Outcome;

        // Assert.
        Assert.Equal(expected.Unreadable, outcome.Unreadable);
        Assert.Equal(expected.Problems, outcome.Problems);
        Assert.Equal(
            expected.Model.Stages.SelectMany(stage => stage.Jobs.SelectMany(job => job.Steps.Select(step => $"{step.Id} {step.Label}").Prepend($"{job.Id} {job.Label}")).Prepend($"{stage.Id} {stage.Label}")),
            outcome.Model.Stages.SelectMany(stage => stage.Jobs.SelectMany(job => job.Steps.Select(step => $"{step.Id} {step.Label}").Prepend($"{job.Id} {job.Label}")).Prepend($"{stage.Id} {stage.Label}")));
        Assert.Equal(
            expected.Model.Unresolved.Select(unresolved => $"{unresolved.Reference.Id} {unresolved.Reference.Reference} {unresolved.Reason}"),
            outcome.Model.Unresolved.Select(unresolved => $"{unresolved.Reference.Id} {unresolved.Reference.Reference} {unresolved.Reason}"));
    }

    [Fact]
    public void ABodyThatIsNotYaml_IsKeptWithTheLineAndTheReadersMessage()
    {
        // Arrange.
        const string text = "stages:\n  - stage: Build\n    jobs: [unclosed\n";
        var expected = PipelineParser.Outcome(LineDocument.Parse(text));

        // Act.
        var body = PipelineBody.Parse(text);

        // Assert.
        Assert.Equal(expected.Unreadable, body.Outcome.Unreadable);
        Assert.StartsWith("Line ", body.Outcome.Unreadable, StringComparison.Ordinal);
        Assert.Equal(expected.Problems, body.Outcome.Problems);
        var reader = Assert.Single(body.ReaderFindings);
        Assert.Equal((expected.Problems[0].Line + 1, expected.Problems[0].Message), (reader.Line, (string)reader.Detail["reason"]!));
    }

    [Fact]
    public void ABodyThatIsYaml_HasNoReaderFinding()
    {
        // Act.
        var body = PipelineBody.Parse("steps:\n  - script: x\n");

        // Assert.
        Assert.True(body.Outcome.IsUsable);
        Assert.Empty(body.ReaderFindings);
        Assert.False(body.IsReadOnly);
    }

    [Fact]
    public void TheDislModel_IsBuiltFromThePluginsReading()
    {
        // Act.
        var body = PipelineBody.Parse("stages:\n  - stage: Build\n    jobs:\n      - job: Compile\n        steps:\n          - script: x\n  - stage: Test\n    dependsOn: Build\n");

        // Assert.
        Assert.Equal(["Build", "Build/Compile", "Build/Compile/step-0", "Test"], body.Disl.Diagram.Nodes.Select(node => node.Id));
        Assert.Same(body.Disl, body.Disl);
        Assert.Contains(body.Disl.Diagram.Relations, relation => relation.Id == "edge:Build->Test");
        Assert.Equal(body.Reading.Elements.Count, PipelineBody.Parse(body.Text).Reading.Elements.Count);
    }

    [Fact]
    public void TheBodyThatReadAModel_IsFoundFromIt_EvenForTwoEmptyReadings()
    {
        // Arrange: two readings that are both the empty pipeline.
        var first = PipelineBody.Parse("");
        var second = PipelineBody.Parse("not: [a pipeline");

        // Act and assert.
        Assert.Same(first, PipelineBody.ReadingOf(first.Model));
        Assert.Same(second, PipelineBody.ReadingOf(second.Model));
        Assert.Throws<InvalidOperationException>(() => PipelineBody.ReadingOf(PipelineModel.Empty));
    }
}
