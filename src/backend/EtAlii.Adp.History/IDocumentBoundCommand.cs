namespace EtAlii.Adp.History;

/// <summary>
/// A command recorded against the text of one document, so that the history can drop it when that
/// document was changed by another program and the record no longer describes it.
/// </summary>
/// <remarks>
/// Implemented by <see cref="RestoreDocumentCommand{TStore}"/>, which is generic in the module's
/// store; the history cannot name that type, and asks this instead.
/// </remarks>
public interface IDocumentBoundCommand : ICommand
{
    /// <summary>The document the command was recorded against.</summary>
    string BodyPath { get; }
}
