using System.Reflection;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;
using Xunit;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads.
/// </summary>
/// <remarks>
/// The property rows and the reparenting refusal derive from it (<c>AnsibleDefinition</c>); the parity transcript
/// is what holds today's behaviour.
/// </remarks>
public sealed partial class BundledDefinitionTests : BundledDefinitionContract<BundledDefinitionTests>, IBundledDefinitions
{
    public static Assembly Module => typeof(Diagram).Assembly;

    public static string ModuleFolder => "ansible-structure";

    public static IReadOnlyList<BundledDefinitionExpectation> Definitions { get; } =
    [
        new("ansible-structure", Diagram.AnsibleStructure.Origin.Key, "net.etalii.adp.ansible.structure")
        {
            Title = Diagram.AnsibleStructure.Title,
            Icon = Diagram.AnsibleStructure.Icon,
        },
    ];

    protected override bool AWarningIsAllowed => true;

    /// <summary>The empty-canvas message the client draws today, from its opening tag to its closing one.</summary>
    [GeneratedRegex("""<EmptyCanvas definition=\{ANSIBLE_DEFINITION\} className="ansible-canvas-message" />""")]
    private static partial Regex EmptyCanvasOfTheDefinition();

    public static TheoryData<string> Folders() => [.. AnsibleTranscript.Corpus()];

    /// <summary>
    /// Every id the definition's <c>derived</c> expressions compute is the id the plugin delivers, element for
    /// element and in order, for every folder of the parity corpus: the model's ids are the definition's, so a
    /// wrong expression fails here rather than in a stored position or a selection.
    /// </summary>
    [Theory]
    [MemberData(nameof(Folders))]
    public void EveryDerivedId_IsThePluginsId(string folder)
    {
        using var copy = TestFolder.Create("EtAlii.Adp.AnsibleBundledDefinition");

        // Arrange.
        var made = copy.Combine("folder");
        AnsibleTranscript.MakeCopy(folder, made);
        var project = new AnsibleProjectReader().Read(made);

        // Act.
        var delivered = AnsibleFolderPlugin.Result(project).Elements.Select(element => $"{element.Type} {element.Id}").ToList();
        var derived = AnsibleModels.ModelOf(project).Elements.Select(element => $"{element.Type.Name} {element.Id}").ToList();

        // Assert.
        Assert.Equal(delivered, derived);
    }

    /// <summary>
    /// The sentence the client shows for a folder with nothing to draw is the definition's <c>canvas.empty</c>:
    /// the canvas compiles its definition from the bundled specification and draws that definition's empty
    /// sentence, so the two cannot differ. Until the switch-over the client wrote the sentence itself and this
    /// compared the two texts; the client's own test asserts the sentence it draws.
    /// </summary>
    [Fact]
    public void TheEmptyCanvasSentence_IsTheOneTheClientDraws()
    {
        // Arrange.
        var canvas = File.ReadAllText(RepositoryPaths.Under("src", "diagrams", "ansible-structure", "client", "AnsibleCanvas.tsx"));

        // Act.
        var stated = Module.ResourceJson("ansible-structure.dis")
            .GetProperty("notation").GetProperty("canvas").GetProperty("empty").GetProperty("text").GetString();

        // Assert.
        Assert.False(string.IsNullOrWhiteSpace(stated));
        Assert.Contains("""import disText from "../definition/ansible-structure.dis?raw";""", canvas, StringComparison.Ordinal);
        Assert.Contains("compileNotation(parseDisl(disText), ANSIBLE_BINDINGS)", canvas, StringComparison.Ordinal);
        Assert.Matches(EmptyCanvasOfTheDefinition(), canvas);
    }
}
