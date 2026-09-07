using Xunit;

namespace EtAlii.Adp.Hierarchy.Tests;

/// <summary>
/// The registration naming convention of adp-file-nesting Requirement 1, and the one thing it
/// deliberately does not do: choose between the two readings of an ambiguous name.
/// </summary>
public class DiagramRegistrationNameTests
{
    [Fact]
    public void AnUnqualifiedName_HasASubjectAndNoQualifier()
    {
        // Act.
        var name = DiagramRegistrationName.TryParse("test.adp");

        // Assert.
        Assert.NotNull(name);
        Assert.Equal("test", name.FullBase);
        Assert.Equal("test", name.SubjectBase);
        Assert.Equal("", name.Qualifier);
        Assert.False(name.IsQualified);
        Assert.False(name.IsFolderScoped);
    }

    [Fact]
    public void AQualifiedName_SplitsOnTheLastDot()
    {
        // Act.
        var name = DiagramRegistrationName.TryParse("test.first.adp");

        // Assert.
        Assert.NotNull(name);
        Assert.Equal("test.first", name.FullBase);
        Assert.Equal("test", name.SubjectBase);
        Assert.Equal("first", name.Qualifier);
        Assert.True(name.IsQualified);
    }

    [Fact]
    public void TheBareExtension_IsFolderScoped()
    {
        // Act.
        var name = DiagramRegistrationName.TryParse(".adp");

        // Assert.
        // Requirement 1.3: the extension with no base name registers the folder it sits in.
        Assert.NotNull(name);
        Assert.True(name.IsFolderScoped);
        Assert.Equal("", name.FullBase);
        Assert.Equal("", name.SubjectBase);
    }

    [Fact]
    public void AnAmbiguousName_ReportsBothReadingsAndChoosesNeither()
    {
        // Act.
        // Requirement 2.2: `my.config.adp` is a subject `my.config` with no qualifier, and also a
        // subject `my` with qualifier `config`. Nothing in the NAME decides which; deciding needs
        // to know what is on disk, so this type reports both and refuses to pick.
        var name = DiagramRegistrationName.TryParse("my.config.adp");

        // Assert.
        Assert.NotNull(name);
        Assert.Equal("my.config", name.FullBase);
        Assert.Equal("my", name.SubjectBase);
        Assert.Equal("config", name.Qualifier);
    }

    [Fact]
    public void ADotFileSubject_IsNotReadAsAQualifier()
    {
        // Act.
        // A leading dot is not a separator: `.hidden.adp` is the subject `.hidden`, not an empty
        // subject with the qualifier `hidden`. A qualifier may not be empty and a subject may
        // legitimately be a dot-file.
        var name = DiagramRegistrationName.TryParse(".hidden.adp");

        // Assert.
        Assert.NotNull(name);
        Assert.Equal(".hidden", name.FullBase);
        Assert.Equal(".hidden", name.SubjectBase);
        Assert.Equal("", name.Qualifier);
        Assert.False(name.IsFolderScoped);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("courier.dsl")]
    [InlineData("adp")]
    public void ANonRegistrationFile_DoesNotParse(string fileName)
    {
        // Act and assert.
        Assert.Null(DiagramRegistrationName.TryParse(fileName));
    }

    [Fact]
    public void Compose_RoundTripsEveryForm()
    {
        // Act and assert, step by step.
        Assert.Equal("test.adp", DiagramRegistrationName.Compose("test", ""));
        Assert.Equal("test.first.adp", DiagramRegistrationName.Compose("test", "first"));
        Assert.Equal(".adp", DiagramRegistrationName.ForFolder());

        var parsed = DiagramRegistrationName.TryParse(DiagramRegistrationName.Compose("test", "second"));
        Assert.NotNull(parsed);
        Assert.Equal("test", parsed.SubjectBase);
        Assert.Equal("second", parsed.Qualifier);
    }

    [Theory]
    [InlineData("a/b", "a-b")]
    [InlineData("a\\b", "a-b")]
    [InlineData("with space", "with-space")]
    [InlineData("dotted.name", "dotted-name")]
    public void AQualifier_IsSanitisedThroughTheOneSanitiser(string qualifier, string expected)
    {
        // Act.
        // Requirement 1.4: a qualifier cannot introduce a path separator or a character the
        // filesystem refuses. The period matters too - it is the separator this convention uses,
        // so a qualifier carrying one would make the composed name ambiguous about where the
        // qualifier begins.
        var composed = DiagramRegistrationName.Compose("test", qualifier);

        // Assert.
        Assert.Equal($"test.{expected}.adp", composed);
    }

    [Fact]
    public void AQualifierCannotEscapeItsFolder()
    {
        // Act.
        var composed = DiagramRegistrationName.Compose("test", "../../../etc/passwd");

        // Assert.
        // The sanitiser is what stands between a qualifier and the rest of the filesystem, so
        // this asserts the outcome rather than the mechanism.
        Assert.DoesNotContain("/", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", composed, StringComparison.Ordinal);
        Assert.DoesNotContain("..", composed, StringComparison.Ordinal);
    }
}
