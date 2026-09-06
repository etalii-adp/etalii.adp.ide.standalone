using EtAlii.Adp.Common;
using Xunit;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// What this module declares itself to be. Small, and worth having: the two unusual halves of
/// this definition - no extension, and a folder subject - are exactly the two core reads to
/// decide what to validate and what a change on disk affects, so a definition that drifted
/// would break the module quietly rather than loudly.
/// </summary>
public class DiagramTests
{
    [Fact]
    public void TheDefinition_IsTheOriginTheCatalogNames()
    {
        // Arrange, act and assert.
        // docs/diagrams.md carries this exact origin; the .adp's first line is its MIME form.
        Assert.Equal("ansible/structure", Diagram.AnsibleStructure.Origin.Key);
        Assert.Equal("ansible/structure", Diagram.AnsibleStructure.Origin.MimeType);
    }

    [Fact]
    public void TheDefinition_DeclaresNoDocumentSibling()
    {
        // Arrange, act and assert.
        // The .adp registration is the whole of what ADP contributes; there is no body file.
        Assert.Equal("", Diagram.AnsibleStructure.Extension);
        Assert.False(Diagram.AnsibleStructure.HasDocumentSibling);
    }

    [Fact]
    public void TheDefinition_SubjectIsTheFolder()
    {
        // Arrange, act and assert.
        Assert.True(Diagram.AnsibleStructure.HasFolderSubject);
        Assert.Equal(DiagramSubject.Folder, Diagram.AnsibleStructure.Subject);
    }

    [Fact]
    public void TheModule_DeclaresExactlyOneNotation()
    {
        // Arrange, act and assert.
        Assert.Same(Diagram.AnsibleStructure, Assert.Single(Diagram.Definitions));
    }

    [Fact]
    public void TheDefinition_SaysWhatTheDiagramAnswers_RatherThanRestatingItsTitle()
    {
        // Arrange, act and assert.
        // The description is shown beside the choice in the Add dialog, to a user who may be
        // meeting the notation for the first time.
        Assert.NotEqual("", Diagram.AnsibleStructure.Description);
        Assert.DoesNotContain(Diagram.AnsibleStructure.Title, Diagram.AnsibleStructure.Description, StringComparison.OrdinalIgnoreCase);
    }
}
