using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Routing;
using EtAlii.Adp.Specification.Fbl.Tests.Plugins;
using EtAlii.Adp.Specification.Fbl.Tests.RealFiles;
using Xunit;

namespace EtAlii.Adp.Specification.Fbl.Tests.Routing;

/// <summary>Templates (FBL §13, Requirement 8.4 and 8.5).</summary>
public class TemplatesTests
{
    public static TheoryData<string, string> DeclaredTemplates()
    {
        var data = new TheoryData<string, string>();
        foreach ((string document, FblBinding binding) in RealFileCorpus.AllBindings())
        {
            if (binding.Template is null || binding.Plugin is not null) continue;
            foreach (var origin in binding.Claims.Origins) data.Add(document, binding.Name + "|" + origin);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(DeclaredTemplates))]
    public void EveryDeclaredTemplateReadsBackWithoutAWarning(string document, string bindingAndOrigin)
    {
        // Arrange.
        (string name, string origin) = (bindingAndOrigin.Split('|')[0], bindingAndOrigin.Split('|')[1]);
        var binding = RealFileCorpus.Binding(document, name);
        var extension = binding.Claims.Extensions.FirstOrDefault() ?? ".txt";

        // Act.
        var bytes = TemplateWriter.Produce(binding, origin, "New plan" + extension, rule => $"ID_{rule}_1")!;
        var model = OpenBody.Open(bytes, binding, new FblOptions { FileName = "New plan" + extension }).Model;

        // Assert.
        Assert.False(model.Unreadable, $"{document}#{name} ({origin}): the template is unreadable.");
        Assert.DoesNotContain(model.Findings, f => f.Severity >= FindingSeverity.Warning);
    }

    [Fact]
    public void ATemplateByOriginWinsOverTheBindingsText()
    {
        // Arrange.
        var turtle = RealFileCorpus.Binding("w3c-turtle.fbl", "turtle");

        // Act.
        var skos = System.Text.Encoding.UTF8.GetString(TemplateWriter.Produce(turtle, "w3c/skos", "Animals 2.ttl", _ => "x")!);
        var rdf = System.Text.Encoding.UTF8.GetString(TemplateWriter.Produce(turtle, "w3c/rdf", "Animals 2.ttl", _ => "x")!);

        // Assert.
        Assert.Contains("ex:Animals_2 a skos:ConceptScheme ;\r\n", skos, StringComparison.Ordinal);
        Assert.Contains("skos:prefLabel \"Animals 2\"@en", skos, StringComparison.Ordinal);
        Assert.Contains("ex:Animals_2 rdfs:label \"Animals 2\" .\r\n", rdf, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Plan", "Plan")]
    [InlineData("my plan-2", "my_plan_2")]
    [InlineData("2024 roadmap", "_2024_roadmap")]
    [InlineData("__x__", "x")]
    [InlineData("---", "untitled")]
    [InlineData("café", "caf")]
    public void TheKeyPlaceholderIsSanitisedExactly(string baseName, string key)
    {
        // Act and assert.
        Assert.Equal(key, TemplateWriter.Key(baseName));
    }

    [Fact]
    public void OnlyTheFourPlaceholdersAreReplaced()
    {
        // Act.
        var text = TemplateWriter.Replace("{name} {base} {key} {newid:node} {id} {newid} { base }", "a b.mm", rule => "N-" + rule);

        // Assert.
        Assert.Equal("a b.mm a b a_b N-node {id} {newid} { base }", text);
    }

    [Fact]
    public void APluginWithoutTemplateTextIsAskedForOne()
    {
        // Arrange.
        var chart = RealFileCorpus.Binding("helm-chart.fbl", "chart");
        var plugin = new FakePlugin("net.etalii.adp.helm.chartFolder");

        // Act.
        var bytes = TemplateWriter.Produce(chart, "helm/chart", "web.yaml", _ => "x", plugin);

        // Assert.
        Assert.Equal("template for web.yaml (web)"u8.ToArray(), bytes);
    }
}
