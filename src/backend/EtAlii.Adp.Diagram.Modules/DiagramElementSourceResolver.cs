using EtAlii.Adp.Documents;
using EtAlii.Adp.Hierarchy;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// Resolves an element of a diagram read from one document of a store: the
/// <see cref="DiagramSnapshotSourceResolver{TSnapshot}"/> with the loading and the following written
/// once for every such module (code-structure FR-032).
/// </summary>
/// <remarks>
/// <b>A module says what its elements are, and nothing else.</b> It describes an element and maps
/// the one element the canvas was sent; the store's entry for the selected file is loaded, and its
/// changes followed, here.
/// </remarks>
/// <typeparam name="TBody">The text of a document as the module's store holds it.</typeparam>
/// <typeparam name="TModel">What the module's parser reads from a body.</typeparam>
public abstract class DiagramElementSourceResolver<TBody, TModel> : DiagramSnapshotSourceResolver<DocumentEntry<TBody, TModel>>
{
    private readonly IReadableDiagramDocumentStore<TBody, TModel> _documents;

    /// <param name="router">Says which diagram type a selected file is.</param>
    /// <param name="documents">The store the elements are read from; only its reading half, because a selection writes nothing.</param>
    protected DiagramElementSourceResolver(DiagramFileRouter router, IReadableDiagramDocumentStore<TBody, TModel> documents)
        : base(router)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <summary>The document whose body is at <paramref name="bodyPath"/>.</summary>
    protected sealed override DocumentEntry<TBody, TModel> Load(string bodyPath) => _documents.GetOrLoad(bodyPath);

    /// <summary>What a selection of <paramref name="elementId"/> shows, or null when nothing has that id.</summary>
    /// <remarks>None by default, for a module that overrides the overload given the whole entry instead.</remarks>
    protected virtual (IReadOnlyList<string> Path, string Text)? Describe(TModel model, string elementId) => null;

    /// <summary>
    /// What a selection of <paramref name="elementId"/> in <paramref name="snapshot"/> shows, or null when
    /// nothing has that id: what <see cref="Describe(TModel, string)"/> says of its model, by default. A
    /// module whose elements read differently in a document that could not be read answers here.
    /// </summary>
    protected override (IReadOnlyList<string> Path, string Text)? Describe(DocumentEntry<TBody, TModel> snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Describe(snapshot.Model, elementId);
    }

    /// <summary>The element <paramref name="elementId"/> as the canvas was sent it, or null when none is drawn.</summary>
    protected abstract DiagramElement? Drawn(TModel model, string elementId);

    /// <inheritdoc />
    protected sealed override DiagramElement? Drawn(DocumentEntry<TBody, TModel> snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return Drawn(snapshot.Model, elementId);
    }

    /// <summary>
    /// What <paramref name="elementId"/> is called when it names a target of the module's own besides
    /// a drop and a connect gesture, such as the diagram itself or a group of one element; null when it
    /// names none. Such a target, like a gesture, is never selected, tracked or written. None by default.
    /// </summary>
    protected virtual string? ProposedOf(TModel model, string elementId) => null;

    /// <inheritdoc />
    protected sealed override string? ProposedOf(DocumentEntry<TBody, TModel> snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ProposedOf(snapshot.Model, elementId);
    }

    /// <summary>Whether <paramref name="elementId"/> has children, for a diagram whose elements form a tree; none by default.</summary>
    protected virtual bool HasChildren(TModel model, string elementId) => false;

    /// <inheritdoc />
    protected sealed override bool HasChildren(DocumentEntry<TBody, TModel> snapshot, string elementId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return HasChildren(snapshot.Model, elementId);
    }

    /// <summary>
    /// Calls <paramref name="changed"/> whenever the document at <paramref name="bodyPath"/> changes
    /// in the store, until the result is disposed. <paramref name="watchId"/> and
    /// <paramref name="elementId"/> are for a module that also follows what each connection has
    /// selected, as supply chain does for its trace; it overrides this and calls it as well.
    /// </summary>
    protected override IDisposable Follow(ShortGuid watchId, string bodyPath, string elementId, Action changed)
    {
        ArgumentNullException.ThrowIfNull(changed);

        var documents = _documents;
        documents.Changed += OnChanged;
        return Disposable.From(() => documents.Changed -= OnChanged);

        void OnChanged(object? sender, DocumentChangedEventArgs args)
        {
            if (string.Equals(args.Path, bodyPath, StringComparison.OrdinalIgnoreCase))
            {
                changed();
            }
        }
    }
}
