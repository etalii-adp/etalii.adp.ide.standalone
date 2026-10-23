using System.Text;
using EtAlii.Adp.Hierarchy;
using EtAlii.Adp.History;
using EtAlii.Adp.TestSupport.Host;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>
/// A new requirement document (spec 029 T072, FR-005, US4 scenario 2): its file is the binding's template with the file's
/// base name as its title, CRLF, byte for byte, and no registration is written beside it.
/// </summary>
public sealed class TemplateTests(HostFixture host) : IClassFixture<HostFixture>
{
    private readonly string _projectFolder = host.NewProjectFolder("new-requirements");

    [Fact]
    public async Task ANewDocument_IsTheTemplate_WithItsNameAsTitle_AndNoRegistration()
    {
        // Arrange: the host's own add, and the template's bytes for a file named lending as the fixtures hold them.
        var definition = host.Services.GetRequiredService<IDesignerDefinitionCatalog>().All.Single(candidate => candidate.Origin == Designer.Origin);
        var format = definition.Formats.Single(candidate => candidate.Extension == ".md");
        var options = host.Services.GetRequiredService<DesignerAddOptions>();
        var expected = await File.ReadAllBytesAsync(IoPath.Combine(HierarchicalRequirementFiles.FixturesFolder, "hierarchical-requirement-new", "lending.md"), TestContext.Current.CancellationToken);

        // Act.
        var plan = options.Plan(definition, format, _projectFolder, "lending");
        var command = Assert.IsType<CreateDocumentBodyCommand>(plan.Command);
        var result = await host.Services.GetRequiredService<IHistoryStackStore>().Get(ProjectRoot.From(_projectFolder)).ExecuteAsync(command, TestContext.Current.CancellationToken);

        // Assert: the Markdown file alone, the template's bytes with its title, and every line ending CRLF.
        Assert.Equal("", plan.Error);
        Assert.True(result.IsSuccess, result.Error);
        Assert.Equal(["lending.md"], Directory.GetFileSystemEntries(_projectFolder).Select(IoPath.GetFileName));
        var written = await File.ReadAllBytesAsync(IoPath.Combine(_projectFolder, "lending.md"), TestContext.Current.CancellationToken);
        Assert.Equal(expected, written);
        var text = Encoding.UTF8.GetString(written);
        Assert.StartsWith("<!-- ded: 0.2 designer: etalii/hierarchical-requirement -->\r\n<!-- last: ITEM-0007 REQ-0009 -->\r\n# lending\r\n", text, StringComparison.Ordinal);
        Assert.Equal(text.Split('\n').Length, text.Split("\r\n").Length);
    }
}
