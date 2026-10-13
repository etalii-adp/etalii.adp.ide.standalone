using System.Text.RegularExpressions;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// Every reader and every schema set the module makes comes from <c>XmlReaders</c>, so the
/// resolver and the limits cannot be forgotten at one call site, and the platform's own resolver
/// is named nowhere (xml-editor Security). Read over the module's source, so a new file is
/// covered on arrival.
/// </summary>
public class ReaderFactoryOnlyTests
{
    private static readonly string ModuleRoot = RepositoryPaths.Under("src", "editors", "xml", "backend", "EtAlii.Adp.Editor.Xml");

    public static TheoryData<string> EverySourceFile()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.EnumerateFiles(ModuleRoot, "*.cs", SearchOption.AllDirectories)
                     .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            data.Add(Path.GetRelativePath(ModuleRoot, file));
        }

        return data;
    }

    [Fact]
    public void TheWalk_FindsTheFactory()
    {
        // Arrange and act: a walk that found nothing would pass every theory below over nothing.
        Assert.Contains("XmlReaders.cs", EverySourceFile().Select(row => row.Data));
    }

    [Theory]
    [MemberData(nameof(EverySourceFile))]
    public async Task NoFileButTheFactory_CreatesAReaderOrASchemaSet(string relativePath)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(ModuleRoot, relativePath), TestContext.Current.CancellationToken);

        // Act.
        string[] patterns =
        [
            @"XmlReader\s*\.\s*Create\s*\(",
            @"new\s+XmlSchemaSet\b",
            @"XmlSchemaSet\s*\(\s*\)",
            @"new\s+XmlTextReader\b",
            @"new\s+XmlReaderSettings\b",
            @"XmlReaderSettings\s*\(\s*\)",
        ];
        var found = relativePath == "XmlReaders.cs" ? [] : patterns.Where(pattern => Regex.IsMatch(text, pattern)).ToArray();

        // Assert.
        Assert.True(found.Length == 0, $"{relativePath} creates a reader or a schema set itself: {string.Join(", ", found)}");
    }

    [Theory]
    [MemberData(nameof(EverySourceFile))]
    public async Task NoFile_NamesThePlatformsResolver_OrSwitchesResolvingOff(string relativePath)
    {
        // Arrange.
        var text = await File.ReadAllTextAsync(Path.Combine(ModuleRoot, relativePath), TestContext.Current.CancellationToken);

        // Act.
        var found = new[] { "XmlUrlResolver", "XmlSecureResolver", "XmlPreloadedResolver", @"XmlResolver\s*=\s*null" }
            .Where(pattern => Regex.IsMatch(text, pattern))
            .ToArray();

        // Assert.
        Assert.True(found.Length == 0, $"{relativePath} names {string.Join(", ", found)}");
    }
}
