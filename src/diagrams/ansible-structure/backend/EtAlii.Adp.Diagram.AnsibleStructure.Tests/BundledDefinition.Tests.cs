using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;
using EtAlii.Adp.Specification.Disl;
using Xunit;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests;

/// <summary>
/// The DISL definition bundled from etalii-adp/etalii.adp is the bytes its provenance says it is,
/// and it loads.
/// </summary>
/// <remarks>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the
/// file on disk - so a build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh ansible-structure &lt;etalii.adp checkout&gt;</c>, never by hand. The
/// property rows and the reparenting refusal derive from it (<c>AnsibleDefinition</c>); the parity transcript
/// is what holds today's behaviour.
/// </remarks>
public sealed partial class BundledDefinitionTests
{
    private static readonly Assembly _module = typeof(Diagram).Assembly;

    [GeneratedRegex("^[0-9a-f]{40}$")]
    private static partial Regex FullRevision();

    /// <summary>The empty-canvas message the client draws today, from its opening tag to its closing one.</summary>
    [GeneratedRegex("""<div className="ansible-canvas-message[^"]*">(?<text>.*?)</div>""", RegexOptions.Singleline)]
    private static partial Regex EmptyCanvasMessage();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    public static TheoryData<string> Folders() => [.. AnsibleTranscript.Corpus()];

    /// <summary>The sha256 in provenance.json is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Fact]
    public void TheBundledDefinition_HashesToItsProvenance()
    {
        var provenance = _module.ResourceJson("ansible-structure.provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(_module.ResourceBytes("ansible-structure.dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The embedded bytes are the bytes of <c>definition/ansible-structure.dis</c>.</summary>
    [Fact]
    public void TheEmbeddedDefinition_EqualsTheDefinitionFolder()
    {
        var onDisk = File.ReadAllBytes(Path.Combine(DefinitionFolder(), "ansible-structure.dis"));

        Assert.Equal(onDisk, _module.ResourceBytes("ansible-structure.dis"));
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Fact]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile()
    {
        var provenance = _module.ResourceJson("ansible-structure.provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/ansible-structure.dis", provenance.GetProperty("path").GetString());
        Assert.Matches(FullRevision(), provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its origin, title and icon equal the catalog's.</summary>
    [Fact]
    public void TheBundledDefinition_IsThisModulesLanguage()
    {
        var language = _module.ResourceJson("ansible-structure.dis").GetProperty("language");

        Assert.Equal("net.etalii.adp.ansible.structure", language.GetProperty("id").GetString());
        Assert.Equal(Diagram.AnsibleStructure.Origin.Key, language.GetProperty("origin").GetString());
        Assert.Equal(Diagram.AnsibleStructure.Title, language.GetProperty("label").GetString());
        Assert.Equal(Diagram.AnsibleStructure.Icon, language.GetProperty("icon").GetString());
    }

    /// <summary>The runtime loads the definition with no diagnostic of severity error.</summary>
    [Fact]
    public void TheBundledDefinition_LoadsWithoutAnError()
    {
        var loaded = BundledDefinition.Load(_module, "ansible-structure.dis");

        Assert.DoesNotContain(loaded.Diagnostics, d => d.Severity == DislSeverity.Error);
    }

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

    /// <summary>The definition's <c>canvas.empty</c> is the sentence the client shows today for a folder with nothing to draw, its markup aside.</summary>
    [Fact]
    public void TheEmptyCanvasSentence_IsTodays()
    {
        // Arrange.
        var canvas = File.ReadAllText(RepositoryPaths.Under("src", "diagrams", "ansible-structure", "client", "AnsibleCanvas.tsx"));
        var match = EmptyCanvasMessage().Match(canvas);
        Assert.True(match.Success, "AnsibleCanvas.tsx draws no ansible-canvas-message.");
        var today = WhiteSpace().Replace(Tag().Replace(match.Groups["text"].Value, ""), " ").Trim();

        // Act.
        var stated = _module.ResourceJson("ansible-structure.dis")
            .GetProperty("notation").GetProperty("canvas").GetProperty("empty").GetProperty("text").GetString();

        // Assert.
        Assert.Equal(today, stated);
    }

    private static string DefinitionFolder()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder != null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, "src", "diagrams", "ansible-structure", "definition");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("src/diagrams/ansible-structure/definition was not found above the test binary.");
    }
}
