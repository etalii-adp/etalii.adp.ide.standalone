using System.Diagnostics;
using System.Text;
using System.Xml;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// The two security guards of xml-editor's Security section, each seen to fail with its
/// protection removed: a document that names a file outside the project root, whose content must
/// appear in nothing the module reads, and a document of nested entities, which must be refused
/// in under a second.
/// </summary>
public sealed class SecurityTests : IDisposable
{
    private const string Canary = "CANARY-5f0c2a-outside-the-project";

    private readonly TestFolder _folder = TestFolder.Create("xml-security");

    public SecurityTests()
    {
        Directory.CreateDirectory(Root);
        File.WriteAllText(_folder.Combine("canary.txt"), Canary);
        File.WriteAllText(_folder.Combine("canary.dtd"), $"<!ENTITY leak \"{Canary}\">");
    }

    public void Dispose() => _folder.Dispose();

    private string Root => _folder.Combine("project");

    [Theory]
    [InlineData("<!DOCTYPE d [<!ENTITY leak SYSTEM \"../canary.txt\">]>\n<d>&leak;</d>")]
    [InlineData("<!DOCTYPE d SYSTEM \"../canary.dtd\">\n<d>&leak;</d>")]
    public void AFileOutsideTheProject_IsNeverRead(string document)
    {
        // Arrange.
        var resolver = new ProjectXmlResolver(Root);
        var read = new StringBuilder();

        // Act: everything the reader yields until it stops, and why it stopped.
        try
        {
            using var reader = XmlReaders.Document(document, Path.Combine(Root, "d.xml"), resolver);
            while (reader.Read())
            {
                read.Append(reader.Value);
            }
        }
        catch (XmlException exception)
        {
            read.Append(exception.Message);
        }

        // Assert: the reference is refused by name, and the canary's content is nowhere.
        Assert.DoesNotContain(Canary, read.ToString(), StringComparison.Ordinal);
        Assert.Contains(resolver.Refused, refused => refused.Reason == "leaves the project");
        Assert.Empty(resolver.Read);
    }

    [Fact]
    public void ADocumentOfNestedEntities_IsRefusedInUnderASecond()
    {
        // Arrange: each level ten of the one below; six levels expand to three million characters,
        // three times the bound.
        var entities = new StringBuilder("<!ENTITY l0 \"lol\">");
        for (var level = 1; level <= 6; level++)
        {
            entities.Append($"<!ENTITY l{level} \"{string.Concat(Enumerable.Repeat($"&l{level - 1};", 10))}\">");
        }

        var document = $"<!DOCTYPE bomb [{entities}]>\n<bomb>&l6;</bomb>";
        var watch = Stopwatch.StartNew();

        // Act.
        var refusal = Assert.Throws<XmlException>(() =>
        {
            using var reader = XmlReaders.Document(document, Path.Combine(Root, "bomb.xml"), new ProjectXmlResolver(Root));
            while (reader.Read())
            {
            }
        });

        // Assert: refused, with the platform's sentence naming the limit, and quickly.
        Assert.Contains("MaxCharactersFromEntities", refusal.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"refusing took {watch.Elapsed.TotalMilliseconds:F0} ms");
    }
}
