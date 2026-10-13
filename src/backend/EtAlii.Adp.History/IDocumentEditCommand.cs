namespace EtAlii.Adp.History;

/// <summary>A command that edits the body of one document.</summary>
/// <remarks>
/// <b>Not <see cref="IDocumentBoundCommand"/>, on purpose.</b> That one tells the history which
/// records to drop when another program writes a document; an edit command recorded as a redo is
/// not dropped today, and taking this interface must not change that.
/// </remarks>
public interface IDocumentEditCommand : ICommand
{
    /// <summary>The document the command edits.</summary>
    string BodyPath { get; }
}
