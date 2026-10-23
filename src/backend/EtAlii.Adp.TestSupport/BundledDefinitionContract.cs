using System.Reflection;
using System.Security.Cryptography;
using EtAlii.Adp.Specification.Disl;
using EtAlii.Adp.Specification.Json;
using Xunit;

namespace EtAlii.Adp.TestSupport;

/// <summary>
/// What a module states of the DISL definitions it bundles from etalii-adp/etalii.adp, which <see cref="BundledDefinitionContract{TModule}"/> holds it to.
/// </summary>
public interface IBundledDefinitions
{
    /// <summary>The module's assembly, which embeds the definitions.</summary>
    static abstract Assembly Module { get; }

    /// <summary>The module's folder under <c>src/diagrams/</c>, whose <c>definition/</c> holds the bundled files.</summary>
    static abstract string ModuleFolder { get; }

    /// <summary>Every definition the module embeds, with what each says of itself.</summary>
    static abstract IReadOnlyList<BundledDefinitionExpectation> Definitions { get; }
}

/// <summary>
/// One bundled definition: its file name without <c>.dis</c>, the catalog origin of the tool type it defines and its language
/// id, and the language header values the module holds it to besides.
/// </summary>
public sealed record BundledDefinitionExpectation(string Name, string Origin, string LanguageId)
{
    /// <summary>The <c>language.label</c>, when the module holds it equal to its catalog title.</summary>
    public string? Title { get; init; }

    /// <summary>The <c>language.icon</c>, when the module holds it equal to its catalog icon.</summary>
    public string? Icon { get; init; }

    /// <summary>The catalog's document extension, dot included, when the module holds <c>language.fileExtension</c> to it.</summary>
    public string? Extension { get; init; }

    /// <summary>The DISL version the definition is written in, when the module depends on one.</summary>
    public string? Disl { get; init; }
}

/// <summary>
/// What every module that bundles a DISL definition must hold of it, written once (code-structure FR-015, FR-052): the embedded
/// bytes are the bytes their provenance records and the bytes in <c>definition/</c>, the provenance names a full revision of the
/// upstream file, the definition is the module's language, it loads, and the module embeds no definition it does not declare.
/// A module's <c>BundledDefinitionTests</c> derives from this with what its definitions hold, and keeps what only it says.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hash is computed over the embedded resource</b>, the bytes the module will load, not over the file on disk, so a
/// build that embedded a stale or rewritten copy fails here too. Re-bundle with
/// <c>src/diagrams/tools/bundle-disl.sh &lt;module&gt; &lt;etalii.adp checkout&gt; [definitions]</c>, never by hand.
/// </para>
/// <para>
/// <b>The module is a type argument rather than an instance, and that is load-bearing.</b> The cases are theories over the
/// module's definitions, and a theory's data is static: <see cref="Names"/> is a static of each closed contract, so every
/// module's cases run over its own definitions and no other's.
/// </para>
/// </remarks>
public abstract class BundledDefinitionContract<TModule>
    where TModule : IBundledDefinitions
{
    /// <summary>The definitions, by file name without its extension.</summary>
    public static TheoryData<string> Names => new(TModule.Definitions.Select(definition => definition.Name));

    /// <summary>Whether the definition may load with a warning; a module whose requirement is "no diagnostic of severity error" says so.</summary>
    protected virtual bool AWarningIsAllowed => false;

    /// <summary>The definitions the module holds once loaded, when it holds them as a set; null when it loads each on its own.</summary>
    protected virtual IReadOnlyCollection<BundledDefinition>? Held => null;

    /// <summary>What the runtime said of <paramref name="loaded"/>, as the module holds it; the loaded definition's own unless the module keeps them apart.</summary>
    protected virtual IReadOnlyList<LoadDiagnostic> DiagnosticsOf(BundledDefinition loaded) => loaded.Diagnostics;

    /// <summary>The definition <paramref name="expected"/> names, loaded as the module loads it.</summary>
    protected virtual BundledDefinition Load(BundledDefinitionExpectation expected) => BundledDefinition.Load(TModule.Module, expected.Name + ".dis");

    /// <summary>The bytes of <paramref name="file"/> in the module's <c>definition/</c> folder.</summary>
    protected static Task<byte[]> BundledFileAsync(string file) =>
        File.ReadAllBytesAsync(RepositoryPaths.Under("src", "diagrams", TModule.ModuleFolder, "definition", file), TestContext.Current.CancellationToken);

    private static BundledDefinitionExpectation Expected(string name) => TModule.Definitions.Single(definition => definition.Name == name);

    /// <summary>A module with one definition keeps its provenance as <c>provenance.json</c>; one with several names each after its definition.</summary>
    private static string ProvenanceFile(string name) => TModule.Definitions.Count == 1 ? "provenance.json" : name + ".provenance.json";

    /// <summary>The sha256 in the provenance is the sha256 of the embedded <c>.dis</c> bytes.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_HashesToItsProvenance(string name)
    {
        var provenance = TModule.Module.ResourceJson(name + ".provenance.json");

        var actual = Convert.ToHexStringLower(SHA256.HashData(TModule.Module.ResourceBytes(name + ".dis")));

        Assert.Equal(provenance.GetProperty("sha256").GetString(), actual);
    }

    /// <summary>The embedded bytes are the bytes of the file in <c>definition/</c>, so what is reviewed is what is loaded.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheEmbeddedDefinition_IsTheBundledFile(string name)
    {
        var onDisk = await BundledFileAsync(name + ".dis");

        Assert.Equal(onDisk, TModule.Module.ResourceBytes(name + ".dis"));
    }

    /// <summary>The embedded provenance is the provenance in <c>definition/</c>.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public async Task TheEmbeddedProvenance_IsTheBundledFile(string name)
    {
        var onDisk = await BundledFileAsync(ProvenanceFile(name));

        Assert.Equal(onDisk, TModule.Module.ResourceBytes(name + ".provenance.json"));
    }

    /// <summary>The provenance names the upstream file and a full 40-hex revision, never a short or symbolic one.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheProvenance_NamesAFullRevisionOfTheUpstreamFile(string name)
    {
        var provenance = TModule.Module.ResourceJson(name + ".provenance.json");

        Assert.Equal("etalii-adp/etalii.adp", provenance.GetProperty("repository").GetString());
        Assert.Equal("definitions/diagrams/" + name + ".dis", provenance.GetProperty("path").GetString());
        Assert.Matches("^[0-9a-f]{40}$", provenance.GetProperty("revision").GetString()!);
    }

    /// <summary>The bundled definition is this module's: its language id, its origin the catalog origin, and the label, icon and extension the module holds it to.</summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_IsThisModulesLanguage(string name)
    {
        var expected = Expected(name);

        var language = TModule.Module.ResourceJson(name + ".dis").GetProperty("language");

        Assert.Equal(expected.LanguageId, language.GetProperty("id").GetString());
        Assert.Equal(expected.Origin, language.GetProperty("origin").GetString());
        if (expected.Title is not null)
        {
            Assert.Equal(expected.Title, language.GetProperty("label").GetString());
        }

        if (expected.Icon is not null)
        {
            Assert.Equal(expected.Icon, language.GetProperty("icon").GetString());
        }

        if (expected.Extension is not null)
        {
            Assert.Equal(expected.Extension, "." + language.GetProperty("fileExtension").GetString());
        }
    }

    /// <summary>
    /// The definition loads as the module loads it, with no diagnostic of severity error and, unless the module allows one, no
    /// warning, and it is the definition of its origin, from its upstream file, at the DISL version the module depends on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Names))]
    public void TheBundledDefinition_LoadsWithoutAnError(string name)
    {
        var expected = Expected(name);

        var loaded = Load(expected);
        var diagnostics = DiagnosticsOf(loaded);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == LoadSeverity.Error);
        if (!AWarningIsAllowed)
        {
            Assert.Empty(diagnostics);
        }

        Assert.Equal(expected.Origin, loaded.Specification.Origin);
        Assert.Equal("definitions/diagrams/" + name + ".dis", loaded.Provenance.Path);
        if (expected.Disl is not null)
        {
            Assert.Equal(expected.Disl, loaded.Specification.Disl);
        }
    }

    /// <summary>The module embeds its definitions and no other, so one more cannot be bundled unnoticed, and holds every one it embeds.</summary>
    [Fact]
    public void TheModule_EmbedsItsDefinitionsAndNoOther()
    {
        var declared = TModule.Definitions.Select(definition => definition.Name + ".dis").Order(StringComparer.Ordinal).ToList();

        var embedded = TModule.Module.GetManifestResourceNames().Where(resource => resource.EndsWith(".dis", StringComparison.Ordinal)).Order(StringComparer.Ordinal);

        Assert.Equal(declared, embedded);
        if (Held is { } held)
        {
            Assert.Equal(declared.Select(file => "definitions/diagrams/" + file), held.Select(definition => definition.Provenance.Path).Order(StringComparer.Ordinal));
        }
    }
}
