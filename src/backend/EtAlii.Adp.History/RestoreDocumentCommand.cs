using EtAlii.Adp.Documents;

namespace EtAlii.Adp.History;

/// <summary>
/// Puts a document's whole text back, byte for byte - the inverse a module edit reports, so undoing
/// it restores comments, formatting and unmodelled constructs exactly as they were
/// (backend-centralization R6). Written once; a module uses it without copying (R6.2).
/// </summary>
/// <typeparam name="TStore">
/// The module's store, which reloads the restored file. <b>It is what keeps the modules apart:</b>
/// the dispatcher finds a handler by the command's type, so one non-generic restore command
/// registered by three modules would reach whichever registered last, and reload the wrong
/// module's store. Closed over each module's store, each module's restore is a type of its own.
/// </typeparam>
/// <param name="BodyPath">The document to restore.</param>
/// <param name="Text">Its complete text as captured before the edit.</param>
/// <param name="Redo">The original command, so redoing the undo runs the same edit again.</param>
/// <param name="After">
/// The document's complete text as the edit left it, or null for a module that does not say.
/// <b>With it, the restore refuses to overwrite a file another program has written since:</b> a
/// file that no longer holds this text was changed by somebody else, and putting
/// <paramref name="Text"/> back would discard that change without a word. Without it the restore
/// writes regardless, as every module's did before agent-activity-diagram R9.6.
/// </param>
/// <remarks>
/// A module registers its handler in one line:
/// <c>services.AddSingleton&lt;ICommandHandler&lt;RestoreDocumentCommand&lt;IMyStore&gt;&gt;, RestoreDocumentCommandHandler&lt;IMyStore&gt;&gt;();</c>
/// </remarks>
public sealed record RestoreDocumentCommand<TStore>(string BodyPath, string Text, ICommand Redo, string? After = null) : IDocumentBoundCommand
    where TStore : IReloadableDocumentStore;
