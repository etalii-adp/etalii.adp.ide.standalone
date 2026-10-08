using System.Text;
using EtAlii.Adp.Editor;

namespace EtAlii.Adp.Diagram;

/// <summary>
/// The one piece of literal interface-bridging the editor family needs: an
/// <see cref="IDiagramSession"/> over a wrapped <see cref="IEditorSession"/>, so the existing
/// <c>DiagramService.Open</c> stream serves a text file with no proto change and no second
/// gRPC surface (modular-text-editors, Non-Functional "no new stream").
/// </summary>
/// <remarks>
/// The whole file is one synthetic element, id <c>"content"</c>, its text carried as a UTF-8
/// payload. An external change replays as a Remove of that element followed by an Add of its
/// successor - NOT the design's "single Update delta", because the delta vocabulary
/// (proto and backend records alike) has no Update arm: Add/Remove/Group/Ungroup is the
/// closed set, and this task is forbidden from widening the proto. The remove-then-add pair
/// expresses the same replacement in the vocabulary that exists; recorded as a deviation from
/// the design's wording in service of the design's own stronger rule.
/// </remarks>
internal sealed class EditorSessionAdapter : IDiagramSession
{
    /// <summary>The one element a text file is.</summary>
    private const string ContentElementId = "content";

    private readonly IEditorSession _session;
    private readonly string _editorId;

    public EditorSessionAdapter(IEditorSession session, string editorId)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(editorId);
        _session = session;
        _editorId = editorId;
        _session.Changed += OnChanged;
    }

    public event EventHandler<DiagramDeltasEventArgs>? Changed;

    public IReadOnlyList<DiagramDelta> Baseline() =>
        [new DiagramAddDelta([ContentElement(_session.Content)])];

    /// <summary>A text file has no viewport semantics: everything is always in view.</summary>
    public IReadOnlyList<DiagramDelta> UpdateView(DiagramViewport viewport) => [];

    public Task<string> MoveElementAsync(string elementId, string newParentId, int index, CancellationToken cancellationToken) =>
        Task.FromResult("A text file has no elements to move.");

    // MoveElementToAsync deliberately not overridden: IDiagramSession's own default already
    // refuses arrangement, which is exactly this adapter's answer.

    private void OnChanged(object? sender, EditorContentChangedEventArgs args) =>
        Changed?.Invoke(this, new DiagramDeltasEventArgs(
        [
            new DiagramRemoveDelta([ContentElementId]),
            new DiagramAddDelta([ContentElement(args.Content)]),
        ]));

    private DiagramElement ContentElement(string content) => new(
        ContentElementId,
        X: 0,
        Y: 0,
        Type: $"editor/{_editorId}",
        PayloadTypeUrl: "type.etalii.adp/editor.content",
        Payload: Encoding.UTF8.GetBytes(content));

    public ValueTask DisposeAsync()
    {
        _session.Changed -= OnChanged;
        return _session.DisposeAsync();
    }
}
