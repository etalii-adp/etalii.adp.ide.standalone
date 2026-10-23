using EtAlii.Adp.Designer;

namespace EtAlii.Adp.Sessions;

/// <summary>
/// One open hierarchy surface on one connection (DESL section 7.7, spec 029 R8): a designer document drawn as one page
/// of nested boxes. The session sends the whole read model on open and again after every change; focus, narrowing and
/// sorting are the client's and never reach it.
/// </summary>
public interface IHierarchySurfaceSession : IDesignerSession
{
    /// <summary>The whole read model as it stands now.</summary>
    HierarchySurfaceModel Model();

    /// <summary>
    /// Raised with the whole model after every change, own or from outside, and with the outcome of each accepted
    /// gesture. A handler must not block - a session may raise this while holding its lock.
    /// </summary>
    event EventHandler<HierarchySurfaceChangedEventArgs>? Changed;

    /// <summary>
    /// Takes one gesture. Returns an empty string when it is accepted, or the sentence saying why it is refused, in
    /// which case nothing changed. What became of an accepted one follows through <see cref="Changed"/>.
    /// </summary>
    /// <param name="editId">The client's id for this gesture; its outcome names it.</param>
    /// <param name="gesture">What the person did.</param>
    string Gesture(ShortGuid editId, HierarchySurfaceGesture gesture);
}
