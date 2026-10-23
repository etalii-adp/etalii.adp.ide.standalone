using EtAlii.Adp.Designer.TableModel;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// One open table on one connection, from a designer module's point of view: what the table
/// is, what changes in it, which lines the connection has in sight and which view it shows, and
/// the author's gestures (knowledge-designer Requirements 10.2 and 10.7).
/// </summary>
/// <remarks>
/// Two tabs on one document are two sessions over the one document the module's store holds, so
/// each has its own window and its own active view.
/// </remarks>
public interface ITableDesignerSession : IDesignerSession
{
    /// <summary>The table as this connection first sees it. The rows follow through <see cref="Changed"/>.</summary>
    TableBaseline Baseline();

    /// <summary>
    /// Raised with what changed: the window's rows, the structure, the findings, or what became
    /// of an edit. A handler must not block - a session may raise this while holding its lock.
    /// </summary>
    event EventHandler<TableChangedEventArgs>? Changed;

    /// <summary>
    /// Which lines the connection has in sight, by index in the active view's order. The session
    /// answers with those lines through <see cref="Changed"/>, and keeps them current.
    /// </summary>
    void SetWindow(int first, int count);

    /// <summary>Which view this connection shows. Answered through <see cref="Changed"/>.</summary>
    void SetActiveView(string viewId);

    /// <summary>
    /// Takes one gesture. Returns an empty string when the edit is accepted - it is then shown
    /// at once and written behind, and <see cref="TableEditSettled"/> says what became of it -
    /// or the sentence saying why it is refused, in which case nothing changed.
    /// </summary>
    /// <param name="editId">The client's id for this edit; its outcome names it.</param>
    /// <param name="gesture">What the author did.</param>
    string Edit(ShortGuid editId, TableGesture gesture);
}
