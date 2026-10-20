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

    /// <summary>
    /// <c>watch</c>, asked after every read: the paths the reading depends on beyond the body, or, for a
    /// folder subject, beyond its file rules. A path names a file, whether or not it exists, or a folder,
    /// whose entries being added or removed changes the reading.
    /// </summary>
    /// <remarks>
    /// Each path is relative to the folder (for a file body, the body's folder) or full;
    /// <see cref="FolderBody.Dependencies"/> and <see cref="PluginBody.WatchPaths"/> name them for the host to watch.
    /// </remarks>
    IReadOnlyList<string> Watch([UsedImplicitly] PluginReadResult last); // last: FBL §11.2's input to watch, the plugin's last read result; no plugin here reads it yet.
}
