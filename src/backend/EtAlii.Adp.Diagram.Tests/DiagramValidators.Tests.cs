using System.Reflection;

using Xunit;

namespace EtAlii.Adp.Diagram.Tests;

public class DiagramValidatorsTests
{
    private static readonly DiagramOrigin Mindmap = new("freeplane", "mindmap");
    private static readonly DiagramOrigin ClassDiagram = new("uml", "class");

    [Fact]
    public void TryGet_ReturnsTheValidatorRegisteredForAnOrigin()
    {
        // Arrange and act.
        var validator = new DiagramValidatorsStubValidator(Mindmap);
        var validators = new DiagramValidators([validator]);

        // Assert.
        Assert.True(validators.TryGet(Mindmap, out var found));
        Assert.Same(validator, found);
    }

    [Fact]
    public void TryGet_IsSilentForAnOriginWithoutRules()
    {
        // Act.
        // A type without a validator is a type without rules - normal, never an error.
        var validators = new DiagramValidators([new DiagramValidatorsStubValidator(Mindmap)]);

        // Assert.
        Assert.False(validators.TryGet(ClassDiagram, out var found));
        Assert.Null(found);
    }

    [Fact]
    public void Construction_ReportsTwoValidatorsClaimingOneOrigin()
    {
        // Arrange and act.
        // A deployment error: silently picking either would judge documents with rules
        // their type never agreed to.
        var exception = Assert.Throws<InvalidOperationException>(
            () => new DiagramValidators([new DiagramValidatorsStubValidator(Mindmap), new DiagramValidatorsOtherStubValidator(Mindmap)]));

        // Assert.
        Assert.Contains("freeplane/mindmap", exception.Message);
        Assert.Contains(nameof(DiagramValidatorsStubValidator), exception.Message);
        Assert.Contains(nameof(DiagramValidatorsOtherStubValidator), exception.Message);
    }

    [Fact]
    public void RulesVersion_IsTheValidatorsOwnAssemblysVersion()
    {
        // Arrange.
        var validators = new DiagramValidators([new DiagramValidatorsStubValidator(Mindmap)]);

        // Act and assert, step by step.
        // The stub lives in this test assembly, so the version must be this assembly's -
        // not core's: a module release may only invalidate its own cached verdicts.
        var expected = typeof(DiagramValidatorsStubValidator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.NotEqual("", expected);
        Assert.Equal(expected, validators.RulesVersion(Mindmap));
    }

    [Fact]
    public void RulesVersion_IsEmptyForAnOriginWithoutRules()
    {
        // Act.
        var validators = new DiagramValidators([]);

        // Assert.
        Assert.Equal("", validators.RulesVersion(Mindmap));
    }

}
