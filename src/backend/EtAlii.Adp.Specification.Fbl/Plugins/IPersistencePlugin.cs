using System.Text.Json;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>
/// The persistence plugin contract of FBL §11.2, as data exchanged through an interface so that a
/// host binds it to its own plugin mechanism. A plugin reads and plans; it never writes files, keeps
/// no undo history and stores no view data (FBL §11.4). No plugin is implemented in this library.
/// </summary>
public interface IPersistencePlugin
{
    /// <summary>The plugin's id, as a binding's <c>reader.plugin</c> names it.</summary>
    string Id { get; }

    /// <summary><c>read</c>: the model of a body, never failing on content.</summary>
    PluginReadResult Read(PluginReadRequest request);

    /// <summary><c>plan</c>: the splices that realise one model change, or the refusal the host shows.</summary>
    PluginPlanResult Plan(PluginPlanRequest request);

    /// <summary><c>template</c>: the bytes of a new body, asked only when the binding has no <c>template.text</c>.</summary>
    byte[] Template(PluginTemplateRequest request);

    /// <summary><c>watch</c>: the paths a folder subject's reading depends on beyond its file rules.</summary>
    IReadOnlyList<string> Watch(PluginReadResult last);
}

/// <summary>One file a plugin reads: the body itself (<see cref="RelativePath"/> empty) or a file of a folder subject.</summary>
public sealed record PluginFile(string RelativePath, byte[] Bytes);

public sealed record PluginReadRequest(IReadOnlyList<PluginFile> Files, JsonElement? Args);

/// <summary>What <c>read</c> delivers: the elements and relations with their source spans, the findings, and whether the body is unreadable.</summary>
public sealed record PluginReadResult(IReadOnlyList<FblElement> Elements, IReadOnlyList<Finding> Findings, bool Unreadable);

public sealed record PluginPlanRequest(IReadOnlyList<PluginFile> Files, PluginReadResult Last, ModelChange Change, JsonElement? Args);

/// <summary>One splice of a plugin's plan, in the file it names (empty for a file body).</summary>
public sealed record PluginSplice(string File, Splice Splice);

/// <summary>What <c>plan</c> delivers: splices, or the sentence the host shows when the change is refused.</summary>
public abstract record PluginPlanResult
{
    public sealed record Planned(IReadOnlyList<PluginSplice> Splices) : PluginPlanResult;

    public sealed record Refused(string Reason) : PluginPlanResult;
}

public sealed record PluginTemplateRequest(string Name, IReadOnlyDictionary<string, string> Placeholders);
