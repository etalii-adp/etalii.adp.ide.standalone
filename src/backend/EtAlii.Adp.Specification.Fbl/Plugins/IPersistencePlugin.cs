using JetBrains.Annotations;

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
    [PublicAPI] // FBL §11.2 names watch as an operation of the persistence plugin contract; no host calls it yet.
    IReadOnlyList<string> Watch([UsedImplicitly] PluginReadResult last); // last: FBL §11.2's input to watch, the plugin's last read result.
}
