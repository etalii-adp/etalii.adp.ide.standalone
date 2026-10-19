using System.Text;
using EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests.Parity;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Fbl;
using EtAlii.Adp.Specification.Fbl.Plugins;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.AzureDevOpsPipeline.Tests;

/// <summary>
/// The persistence plugin reads every pipeline as <see cref="PipelineParser"/> reads it (azure-devops-pipeline-disl-fbl
/// Requirement 4): the binding types in the parser's order, so the definition derives the parser's ids; every value
/// the text it was written with; templates followed inside the workspace only, once per reading, and their parameters
/// by name; a body that is not YAML as one finding; and never a throw.
/// </summary>
public class PipelineYamlPluginTests : IDisposable
{
    private readonly string _workspace = IoPath.Combine(IoPath.GetTempPath(), "adp-pipeline-plugin-" + Guid.NewGuid().ToString("N"));

    public PipelineYamlPluginTests() => Directory.CreateDirectory(_workspace);

    public void Dispose()
    {
        TestFolder.TryDelete(_workspace);
        GC.SuppressFinalize(this);
    }

    public static TheoryData<string> Documents() => [.. PipelineCorpus.Documents().Select(document => document.Relative)];

    [Theory]
    [MemberData(nameof(Documents))]
    public void TheDerivedIds_AreTheParsersIds(string path)
    {
        // Arrange: the corpus document read in its own folder, so the templates it names are followed.
        (PipelineBody body, PipelineModel parsed) = OpenInPlace(path);
        var expected = parsed.Stages
            .SelectMany(stage => stage.Jobs
                .SelectMany(job => job.Steps.Select(step => $"{step.Id} {step.DisplayName} {step.Identifier}").Prepend($"{job.Id} {job.DisplayName} "))
                .Prepend($"{stage.Id} {stage.DisplayName} "))
            .Concat(parsed.Unresolved.Select(unresolved => $"template:{unresolved.Reference.Id} {unresolved.Reference.Path} "))
            .ToList();

        // Act: each node's id with what says which element it is.
        var actual = body.Disl.Diagram.Nodes
            .Select(node => $"{node.Id} {node.Attributes.GetValueOrDefault(node.IsA("TemplateReference") ? "path" : "displayName")} {node.Attributes.GetValueOrDefault("identifier")}")
            .ToList();

        // Assert.
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("templates.yml")]
    [InlineData("template-in-template.yml")]
    public void AFollowedTemplate_IsReadInPlace_SoPositionsCountAsTheParsersDo(string name)
    {
        // Arrange.
        (PipelineBody body, PipelineModel parsed) = OpenInPlace(FixturePath(name));

        // Act.
        var stages = body.Disl.Diagram.NodesOfType("Stage");

        // Assert: the jobs and steps a template holds are where the template is, under the stage that names it.
        Assert.Equal(parsed.Stages.Select(stage => stage.Id), stages.Select(stage => stage.Id));
        foreach ((DislElement node, PipelineStage stage) in stages.Zip(parsed.Stages))
        {
            Assert.Equal(stage.Jobs.SelectMany(job => job.Steps.Select(step => step.Id).Prepend(job.Id)), node.Descendants().Select(child => child.Id));
        }

        Assert.Contains(parsed.Steps, step => step.IsFromTemplate);
        Assert.Equal(parsed.Templates.Count, parsed.Templates.Select(reference => reference.Id).Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("multi-stage.yml", "Stage", "Job", "Step")]
    [InlineData("jobs-only.yml", "ImplicitStage", "Job", "Step")]
    [InlineData("steps-only.yml", "ImplicitStage", "ImplicitJob", "Step")]
    public void TheThreeShapesOfAPipeline_AreTheBindingsTypes(string name, string stage, string job, string step)
    {
        // Act.
        var reading = Read(name);

        // Assert.
        Assert.Equal("Pipeline", reading.Elements[0].Type);
        Assert.Equal(stage, reading.Elements[1].Type);
        Assert.Equal(job, reading.Elements[2].Type);
        Assert.Equal(step, reading.Elements[3].Type);
        Assert.All(reading.Elements, element => Assert.False(element.IdIsStored));
    }

    [Fact]
    public void ADeploymentJobAndATemplateNotFollowed_AreTheirOwnTypes()
    {
        // Act.
        var deployment = Read("findings/deployment-on-hooks.yml");
        var templates = Read("templates.yml");

        // Assert.
        Assert.Contains(deployment.Elements, element => element.Type == "DeploymentJob");
        var reference = Assert.Single(templates.Elements, element => element.Type == "TemplateReference");
        Assert.Equal("otherRepository", reference.Attributes["reason"]);
    }

    [Fact]
    public void ADependency_IsOneRelationPerWrittenName_ResolvedWithoutCase_InItsScope()
    {
        // Act.
        var reading = Read("findings/depends-on-case.yml");
        var byId = reading.Elements.ToDictionary(element => element.Id);
        var relations = reading.Elements.Where(element => element.IsRelation).ToList();

        // Assert: Test waits for build, Unit for lint, and Package's Compile is a job of another stage.
        Assert.All(relations, relation => Assert.Equal("DependsOn", relation.Type));
        Assert.Equal(
            ["build Build -> Test", "lint Lint -> Unit", "Compile (unset) -> Package"],
            relations.Select(relation => $"{relation.Attributes["name"]} {(relation.Source is { } source ? byId[source].Attributes["name"] : "(unset)")} -> {byId[relation.Target!].Attributes["name"]}"));
    }

    [Fact]
    public void ADependencyThatNamesNothing_LeavesItsEndUnset()
    {
        // Act.
        var relation = Assert.Single(Read("findings/dangling-dependency.yml").Elements, element => element.IsRelation);

        // Assert.
        Assert.Equal("Pacakge", relation.Attributes["name"]);
        Assert.Null(relation.Source);
        Assert.NotNull(relation.Target);
    }

    [Fact]
    public void EveryValue_IsTheTextItWasWrittenWith()
    {
        // Act.
        var reading = Read("findings/scalars-as-text.yml");
        var stage = Assert.Single(reading.Elements, element => element.Type == "Stage");
        var job = Assert.Single(reading.Elements, element => element.Type == "Job");
        var step = Assert.Single(reading.Elements, element => element.Type == "Step");

        // Assert: what YAML's core schema would make a number, a boolean or nothing is text (finding B1).
        Assert.Equal(("1.0", "yes"), ((string)stage.Attributes["name"]!, (string)stage.Attributes["displayName"]!));
        Assert.Equal(("null", "0x10"), ((string)job.Attributes["name"]!, (string)job.Attributes["displayName"]!));
        Assert.Equal("True", step.Attributes["enabled"]);
        Assert.Equal("echo ${{ parameters.flag }}", step.Attributes["identifier"]);
    }

    [Fact]
    public void NoExpression_IsEvaluated()
    {
        // Act.
        var reading = Read("edge-expressions.yml");
        var stages = reading.Elements.Where(element => element.Type == "Stage").ToDictionary(element => (string)element.Attributes["name"]!);

        // Assert.
        Assert.Equal("${{ format('Build {0}', parameters.deploy) }}", stages["Build"].Attributes["displayName"]);
        Assert.Equal("${{ if parameters.deploy }}", stages["Deploy"].Attributes["gate"]);
        Assert.Equal("and(succeeded(), eq(variables['Build.Reason'], 'Manual'))", stages["Deploy"].Attributes["condition"]);
        Assert.Equal("eq(dependencies.Build.outputs['Compile.result'], 'Succeeded')", stages["Report"].Attributes["condition"]);
    }

    [Fact]
    public void ATemplatesParameters_AreReadByNameAndNeverByValue()
    {
        // Act.
        var reading = Read("templates.yml");

        // Assert: templates.yml passes environment: production to the template it cannot follow.
        var reference = Assert.Single(reading.Elements, element => element.Type == "TemplateReference");
        Assert.Equal(["environment"], (IEnumerable<object?>)reference.Attributes["parameterNames"]!);
        Assert.DoesNotContain(reading.Elements.SelectMany(element => Texts(element.Attributes.Values)), text => text.Contains("production", StringComparison.Ordinal));
    }

    [Fact]
    public void ATemplateOutsideTheWorkspace_IsNeverRead()
    {
        // Arrange: a template that exists, beside the workspace rather than in it.
        var outside = _workspace + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(IoPath.Combine(outside, "jobs.yml"), "jobs:\n  - job: Escaped\n    steps:\n      - script: x\n");
            var path = Write("azure-pipelines.yml", $"stages:\n  - stage: Build\n    jobs:\n      - template: ../{IoPath.GetFileName(outside)}/jobs.yml\n");
            var templates = new PipelineTemplates(ProjectRoot.From(_workspace));

            // Act.
            var body = PipelineBody.Open(File.ReadAllBytes(path), new PipelineReadContext(path, templates));

            // Assert.
            Assert.Equal(0, templates.FilesRead);
            Assert.DoesNotContain(body.Model.Jobs, job => job.Name == "Escaped");
            var reference = Assert.Single(body.Reading.Elements, element => element.Type == "TemplateReference");
            Assert.Equal("outsideWorkspace", reference.Attributes["reason"]);
        }
        finally
        {
            TestFolder.TryDelete(outside);
        }
    }

    [Fact]
    public void AnExtendsNotFollowed_IsTheTemplateReferenceTemplateExtends_AndItsFindingIsLocatedThere()
    {
        // Arrange: an extends naming a repository resource, which is never followed. The parser calls it
        // "extends" rather than numbering it, and the hand-written mapper draws it as template:extends;
        // reading it once threw, and then numbered it after the other references, template:template-0.
        (PipelineBody body, _) = OpenInPlace(FixturePath("extends-other-repository.yml"));

        // Act.
        var findings = PipelineDefinition.Findings(body);

        // Assert.
        var reference = Assert.Single(body.Reading.Elements, element => element.Type == "TemplateReference");
        Assert.Equal(("extends", "otherRepository", "templates"), ((string)reference.Attributes["slot"]!, (string)reference.Attributes["reason"]!, (string)reference.Attributes["resource"]!));
        Assert.Equal(["template:extends"], body.Disl.Diagram.Nodes.Select(node => node.Id));
        var finding = Assert.Single(findings, each => each.Code == PipelineRules.TemplateNotFollowed);
        Assert.Equal(["template:extends"], finding.ElementIds);
    }

    [Fact]
    public void WithinOneReading_ATemplateIsReadOnce()
    {
        // Arrange: one steps template named by two jobs and by a jobs template, so the reading meets it three times.
        Write("templates/steps.yml", "steps:\n  - script: echo shared\n");
        Write("templates/jobs.yml", "jobs:\n  - job: Nested\n    steps:\n      - template: steps.yml\n");
        var path = Write(
            "azure-pipelines.yml",
            "stages:\n  - stage: Build\n    jobs:\n      - job: One\n        steps:\n          - template: templates/steps.yml\n"
            + "      - job: Two\n        steps:\n          - template: templates/steps.yml\n      - template: templates/jobs.yml\n");
        var templates = new PipelineTemplates(ProjectRoot.From(_workspace));

        // Act.
        var body = PipelineBody.Open(File.ReadAllBytes(path), new PipelineReadContext(path, templates));
        _ = (body.Model, body.Reading, body.Disl, body.Outcome, PipelineDefinition.Findings(body));

        // Assert: two templates, each read once, and all three uses of the shared one followed.
        Assert.Equal(2, templates.FilesRead);
        Assert.Equal(3, body.Model.Steps.Count(step => step.Identifier == "echo shared"));
    }

    [Fact]
    public void ABodyThatIsNotYaml_IsOneFindingOnTheLineOfTheProblem_AndUnreadable()
    {
        // Arrange.
        var text = File.ReadAllText(IoPath.Combine(PipelineCorpus.SourceFolder, FixturePath("findings/not-yaml.yml")));
        var expected = PipelineParser.Outcome(LineDocument.Parse(text)).Problems.Single();

        // Act.
        var body = PipelineBody.Parse(text);

        // Assert.
        Assert.True(body.Reading.Unreadable);
        Assert.Empty(body.Reading.Elements);
        var finding = Assert.Single(body.Reading.Findings);
        Assert.Equal((PipelineValidator.UnparseableRuleId, FindingSeverity.Error), (finding.Code, finding.Severity));
        Assert.Equal($"This is not YAML that can be read: {expected.Message}", finding.Message);
        Assert.Equal(expected.Line + 1, finding.Location!.Line);
        Assert.True(body.IsReadOnly);
        Assert.Empty(body.Model.Stages);
        Assert.Empty(body.Disl.Diagram.Nodes);
    }

    [Theory]
    [InlineData("jobs:\n  - job: Compile\n    steps:\n      - script: [otnet build\n        displayName: Build\n")]
    [InlineData("stages:\n  - stage: Build\n    jobs:\n      : job: null\n        steps:\n          - script: x\n")]
    public void TextTheYamlReaderRefusesWithoutAYamlException_IsUnparseable(string text)
    {
        // Arrange: YamlDotNet refuses the first with an InvalidOperationException and the second with an
        // ArgumentException, which reading used to let through.

        // Act.
        var body = PipelineBody.Parse(text);

        // Assert.
        Assert.True(body.Reading.Unreadable);
        var finding = Assert.Single(body.Reading.Findings);
        Assert.Equal(PipelineValidator.UnparseableRuleId, finding.Code);
        Assert.StartsWith("This is not YAML that can be read: ", finding.Message, StringComparison.Ordinal);
        Assert.False(body.Outcome.IsUsable);
    }

    [Theory]
    [MemberData(nameof(Documents))]
    public void Reading_NeverThrows_OnTruncatedOrGarbledCopies(string path)
    {
        // Arrange: every cut of the document, and copies with one byte made into something YAML gives meaning to.
        var document = PipelineCorpus.Documents().Single(candidate => candidate.Relative == path);
        var full = IoPath.Combine(PipelineCorpus.SourceFolder, document.Relative);
        var bytes = File.ReadAllBytes(full);
        var context = new PipelineReadContext(full, new PipelineTemplates(ProjectRoot.From(IoPath.Combine(PipelineCorpus.SourceFolder, document.Root))));
        var random = new Random(bytes.Length);
        var copies = new List<byte[]>();
        for (var length = 0; length <= bytes.Length; length++)
        {
            copies.Add(bytes[..length]);
        }

        var meaningful = ":-{}[]&*!|>#'\"\t\n ?,%@`"u8.ToArray();
        for (var copy = 0; copy < 200 && bytes.Length > 0; copy++)
        {
            var garbled = (byte[])bytes.Clone();
            for (var change = 0; change < 1 + (copy % 4); change++)
            {
                garbled[random.Next(garbled.Length)] = meaningful[random.Next(meaningful.Length)];
            }

            copies.Add(garbled);
        }

        // Act and assert.
        foreach (var copy in copies)
        {
            foreach (var reading in new[] { context, PipelineReadContext.Unbound })
            {
                var exception = Record.Exception(() =>
                {
                    var body = PipelineBody.Open(copy, reading);
                    _ = (body.Disl, body.Outcome, PipelineDefinition.Findings(body));
                });
                Assert.True(exception is null, $"{path}, {copy.Length} bytes: {exception}\n{Encoding.UTF8.GetString(copy)}");
            }
        }
    }

    [Fact]
    public void Watch_NamesNothing()
    {
        // Arrange.
        var plugin = new PipelineYamlPlugin(PipelineReadContext.Unbound);

        // Act.
        var read = plugin.Read(new PluginReadRequest([new PluginFile("", "steps:\n  - script: x\n"u8.ToArray())]));

        // Assert.
        Assert.Empty(plugin.Watch(read));
    }

    private static string FixturePath(string name) =>
        IoPath.GetRelativePath(PipelineCorpus.SourceFolder, IoPath.Combine(PipelineCorpus.ModuleFolder, "backend", PipelineCorpus.TestProject, "Fixtures", name)).Replace('\\', '/');

    /// <summary>The fixture <paramref name="name"/> read through the plugin, in the fixtures' folder.</summary>
    private static FblModel Read(string name) => OpenInPlace(FixturePath(name)).Body.Reading;

    /// <summary>The corpus document at <paramref name="path"/> read in its own folder, and the parser's reading of it.</summary>
    private static (PipelineBody Body, PipelineModel Parsed) OpenInPlace(string path)
    {
        var document = PipelineCorpus.Documents().Single(candidate => candidate.Relative == path);
        var full = IoPath.Combine(PipelineCorpus.SourceFolder, document.Relative);
        var root = IoPath.Combine(PipelineCorpus.SourceFolder, document.Root);
        var body = PipelineBody.Open(File.ReadAllBytes(full), new PipelineReadContext(full, new PipelineTemplates(ProjectRoot.From(root))));
        var parsed = PipelineParser.Outcome(LineDocument.Parse(File.ReadAllText(full)), new PipelineTemplates(ProjectRoot.From(root)), full).Model;
        return (body, parsed);
    }

    private static IEnumerable<string> Texts(IEnumerable<object?> values) =>
        values.SelectMany(value => value switch
        {
            string text => [text],
            IDictionary<string, object?> map => Texts(map.Values),
            IEnumerable<object?> list => Texts(list),
            _ => [],
        });

    private string Write(string relativePath, string content)
    {
        var full = IoPath.Combine(_workspace, relativePath.Replace('/', IoPath.DirectorySeparatorChar));
        Directory.CreateDirectory(IoPath.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }
}
