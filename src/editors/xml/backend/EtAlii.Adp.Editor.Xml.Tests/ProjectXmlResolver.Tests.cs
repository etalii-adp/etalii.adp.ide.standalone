using System.Xml;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// The resolver every reader and schema set is given: references resolve against the folder of
/// the file that names them, only files of the project are read, and both what was read and what
/// was refused are recorded (xml-editor Requirement 5.4 and Security).
/// </summary>
public sealed class ProjectXmlResolverTests : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("xml-resolver");

    public void Dispose() => _folder.Dispose();

    private string Root => _folder.Combine("project");

    [Fact]
    public void AReference_ResolvesAgainstTheFolderOfTheFileThatNamesIt()
    {
        // Arrange.
        var resolver = new ProjectXmlResolver(ProjectRoot.From(Root));
        var document = new Uri(ProjectXmlResolver.BaseUriOf(Path.Combine(Root, "settings", "app.xml")));

        // Act.
        var resolved = resolver.ResolveUri(document, "../schemas/app.xsd");

        // Assert.
        Assert.Equal(Path.Combine(Root, "schemas", "app.xsd"), resolved.LocalPath);
        Assert.Empty(resolver.Refused);
    }

    [Theory]
    [InlineData("../../outside.xsd", "leaves the project")]
    [InlineData("http://example.com/app.xsd", "is a web address; ADP reads schemas from the project")]
    [InlineData("file:///etc/passwd", "is not a file of the project")]
    [InlineData("/etc/passwd", "is an absolute path; ADP reads a schema relative to the document")]
    public void AReferenceThatIsNotAFileOfTheProject_IsRefusedWithItsReason_AndReadingItFails(string reference, string reason)
    {
        // Arrange.
        var resolver = new ProjectXmlResolver(ProjectRoot.From(Root));
        var document = new Uri(ProjectXmlResolver.BaseUriOf(Path.Combine(Root, "app.xml")));

        // Act.
        var resolved = resolver.ResolveUri(document, reference);
        var failure = Assert.Throws<XmlException>(() => resolver.GetEntity(resolved, null, typeof(Stream)));

        // Assert.
        Assert.Equal(new RefusedReference(reference, reason), Assert.Single(resolver.Refused));
        Assert.Equal($"'{reference}' {reason}.", failure.Message);
        Assert.Empty(resolver.Read);
    }

    [Fact]
    public void AnAddressOutsideTheProject_IsNotRead_EvenWhenAskedForDirectly()
    {
        // Arrange: a reader may ask for an entity by an address it built itself.
        Directory.CreateDirectory(Root);
        File.WriteAllText(_folder.Combine("outside.txt"), "outside");
        var resolver = new ProjectXmlResolver(ProjectRoot.From(Root));

        // Act and assert.
        Assert.Throws<XmlException>(() => resolver.GetEntity(new Uri(_folder.Combine("outside.txt")), null, typeof(Stream)));
        Assert.Empty(resolver.Read);
    }

    [Fact]
    public void AFileReachedThroughALinkThatLeavesTheProject_IsNotRead()
    {
        // Arrange: a folder of the project that is a link to one outside it, which the project's
        // root refuses as it refuses `..` (code-structure B-11).
        Directory.CreateDirectory(Root);
        var elsewhere = _folder.Combine("elsewhere");
        Directory.CreateDirectory(elsewhere);
        File.WriteAllText(Path.Combine(elsewhere, "app.xsd"), "<schema/>");
        CreateLinkOrSkip(Path.Combine(Root, "schemas"), elsewhere);
        var resolver = new ProjectXmlResolver(ProjectRoot.From(Root));

        // Act and assert.
        Assert.Throws<XmlException>(() => resolver.GetEntity(new Uri(Path.Combine(Root, "schemas", "app.xsd")), null, typeof(Stream)));
        Assert.Empty(resolver.Read);
    }

    // Windows lets only an elevated process or one in developer mode create a symbolic link.
    private static void CreateLinkOrSkip(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException && OperatingSystem.IsWindows())
        {
            Assert.Skip($"This process may not create a symbolic link: {exception.Message}");
        }
    }

    [Fact]
    public void AFileOfTheProject_IsReadThroughTheSharedBuffer_AndRecorded()
    {
        // Arrange.
        Directory.CreateDirectory(Path.Combine(Root, "schemas"));
        var schema = Path.Combine(Root, "schemas", "app.xsd");
        File.WriteAllText(schema, "<schema/>");
        var resolver = new ProjectXmlResolver(ProjectRoot.From(Root));

        // Act.
        using var stream = Assert.IsType<MemoryStream>(resolver.GetEntity(new Uri(schema), null, typeof(Stream)));

        // Assert.
        Assert.Equal("<schema/>", new StreamReader(stream).ReadToEnd());
        Assert.Equal([schema], resolver.Read);
    }

    [Fact]
    public void AFileTheSharedBufferRefuses_IsNotRead()
    {
        // Arrange: not UTF-8, so the family's buffer refuses it, and so does the resolver.
        Directory.CreateDirectory(Root);
        var schema = Path.Combine(Root, "latin.xsd");
        File.WriteAllBytes(schema, [0x3C, 0x61, 0xE9, 0x2F, 0x3E]);
        var resolver = new ProjectXmlResolver(ProjectRoot.From(Root));

        // Act.
        var failure = Assert.Throws<XmlException>(() => resolver.GetEntity(new Uri(schema), null, typeof(Stream)));

        // Assert.
        Assert.Contains("not UTF-8 or ASCII", failure.Message, StringComparison.Ordinal);
        Assert.Empty(resolver.Read);
    }
}
