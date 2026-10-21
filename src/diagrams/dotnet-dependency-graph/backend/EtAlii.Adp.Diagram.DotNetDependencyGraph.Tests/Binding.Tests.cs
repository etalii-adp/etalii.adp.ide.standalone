using System.Reflection;
using System.Text;
using EtAlii.Adp.Specification.Fbl.Documents;
using Xunit;

namespace EtAlii.Adp.Diagram.DotNetDependencyGraph.Tests;

/// <summary>
/// The module's own binding, <c>dotnet-dependency-graph.fbl</c>, embedded (dotnet-dependency-graph-disl-fbl, task 4,
/// Requirements 2.1, 2.2 and 2.4): it loads with no problem, the definition's <c>persistence.binding</c> names it, it
/// hands the solution to the plugin with no rule of its own, and the refusal of a write is stated once, in the
/// definition.
/// </summary>
/// <remarks>
/// These tests hold what the binding says; what a reading through it gives is held by <c>DotNetSolutionPlugin.Tests.cs</c>
/// and the builder parity test (task 5).
/// </remarks>
public sealed class BindingTests
{
    private const string BindingResource = "dotnet-dependency-graph.fbl";

    private const string BindingName = "solution";

    /// <summary>Today's sentence for a refused write, as a user reads it.</summary>
    private const string TodaysRefusal = "A .NET dependency graph shows the solution as it is and changes nothing in it. Edit the project file this value comes from, and the diagram will follow.";

    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [Fact]
    public void TheEmbeddedBinding_LoadsWithNoProblem()
    {
        // Act.
        var problems = FblDocumentLoader.Load(_module.ResourceBytes(BindingResource), out var document);

        // Assert.
        Assert.Empty(problems);
        Assert.True(document!.Bindings.ContainsKey(BindingName));
    }

    [Fact]
    public void TheDefinitionsBinding_NamesTheEmbeddedBinding()
    {
        // Arrange.
        var persistence = _module.ResourceJson("dotnet-dependency-graph.dis").GetProperty("persistence");

        // Act.
        var reference = persistence.GetProperty("binding").GetString()!;

        // Assert: the module's own file, by the path it is embedded from, and a binding that file holds.
        Assert.Equal("fbl", persistence.GetProperty("format").GetString());
        Assert.EndsWith("/src/diagrams/dotnet-dependency-graph/backend/EtAlii.Adp.Diagram.DotNetDependencyGraph/" + BindingResource + "#" + BindingName, reference, StringComparison.Ordinal);
        Assert.True(Document().Bindings.ContainsKey(reference[(reference.LastIndexOf('#') + 1)..]));
    }

    [Fact]
    public void TheBinding_HandsTheSolutionToThePlugin_WithNoRuleOfItsOwn()
    {
        // Act.
        var binding = Binding();

        // Assert.
        Assert.Equal("net.etalii.adp.dotnet.solution", binding.Plugin?.Plugin);
        Assert.Empty(binding.Elements);
        Assert.Empty(binding.Relations);
        Assert.False(binding.Body.IsFolder);
        Assert.Equal(250, binding.Body.Settle);
    }

    [Fact]
    public void TheBinding_ClaimsBothSerializations_SharedAndThroughARegistrationOnly()
    {
        // Act.
        var claims = Binding().Claims;

        // Assert: a solution in the workspace is not a diagram until the user adds one, as today.
        Assert.Equal([Diagram.DocumentExtension, Diagram.AlternateDocumentExtension], claims.Extensions);
        Assert.Equal([Diagram.DependencyGraph.Origin.Key], claims.Origins);
        Assert.True(claims.Shared);
        Assert.True(claims.RegistrationOnly);
    }

    [Fact]
    public void TheBinding_IsReadOnly_NeverCreatesOnFirstPlacement_AndHasATemplate()
    {
        // Arrange.
        var json = _module.ResourceJson(BindingResource).GetProperty("bindings").GetProperty(BindingName);

        // Act.
        var binding = Binding();

        // Assert.
        Assert.NotNull(binding.ReadOnly);
        Assert.False(json.GetProperty("registration").GetProperty("createOnFirstPlacement").GetBoolean());
        Assert.Equal("<Solution>\r\n</Solution>\r\n", binding.Template?.Text);
    }

    [Fact]
    public void TheRefusal_IsStatedOnce_InTheDefinition_AndReadsAsToday()
    {
        // Arrange.
        var bindingText = Encoding.UTF8.GetString(_module.ResourceBytes(BindingResource));
        var json = _module.ResourceJson(BindingResource).GetProperty("bindings").GetProperty(BindingName);

        // Act.
        var refusal = DotNetDefinition.Refusal;

        // Assert: the definition's std.readOnly gives today's text, and the binding says only that it is read-only.
        Assert.Equal(TodaysRefusal, refusal);
        Assert.True(json.GetProperty("readOnly").GetBoolean());
        foreach (var sentence in refusal.Split(". ", StringSplitOptions.RemoveEmptyEntries).Select(sentence => sentence.TrimEnd('.')))
        {
            Assert.DoesNotContain(sentence, bindingText, StringComparison.Ordinal);
        }
    }

    private static FblDocument Document()
    {
        var problems = FblDocumentLoader.Load(_module.ResourceBytes(BindingResource), out var document);
        Assert.Empty(problems);
        return document!;
    }

    private static FblBinding Binding() => Document().Bindings[BindingName];
}
