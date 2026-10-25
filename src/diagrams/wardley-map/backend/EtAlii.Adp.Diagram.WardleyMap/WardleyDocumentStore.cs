using System.Collections.Concurrent;
using EtAlii.Adp.Documents;
using EtAlii.Adp.Files;

namespace EtAlii.Adp.Diagram.WardleyMap;

/// <inheritdoc cref="IWardleyDocumentStore" />
/// <remarks>
/// <para>
/// <b>The shared store, told how a Wardley map reads (code-structure FR-029).</b> Opening, the
/// retries before a refused read is believed, keeping the last good document through a reload that
/// cannot read (R2.4), clearing it only on the watcher's delete (R2.5), ignoring this store's own
/// save (R2.3), the atomic publish (Requirement 3.6), refusing to write an entry standing in for a
/// body that could not be read, and telling the sessions are all
/// <see cref="LifecycleDocumentStore{TBody, TModel}"/>'s. What stays here is what is this module's:
/// how a body's text becomes a document and a map, and the identities held beside it with their
/// sidecar.
/// </para>
/// <para>
/// <b>The identities are re-reconciled only when a document was installed or saved</b>. A reload
/// that kept the last good document changed nothing a session shows, so the identities it was
/// holding still match it.
/// </para>
/// </remarks>
public sealed class WardleyDocumentStore(IFileWriter writer) : LifecycleDocumentStore<LineDocument, WardleyMap>(writer), IWardleyDocumentStore
{
    /// <summary>How many texts of one map the store remembers the identities of, for an undo and a redo.</summary>
    private const int Remembered = 64;

    private readonly ConcurrentDictionary<string, Held> _identities = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Memory> _memories = new(StringComparer.OrdinalIgnoreCase);

    private readonly WardleyIdentities _sidecar = new(writer);

    /// <inheritdoc />
    public IReadOnlyList<WardleyIdentityEntry> Identities(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (_identities.TryGetValue(path, out var held))
        {
            return held.Entries;
        }

        // Reconciled against the document this store holds, never read here: a caller asks for the
        // identities of a map it has already read, so a map that is not held has none yet, and none
        // are kept for it until it is (code-structure FR-050).
        return Get(path) is { } entry
            ? _identities.GetOrAdd(path, key => new Held(entry.Document.Text, entry.Model, WardleyIdentities.Reconcile(entry.Model, _sidecar.Read(key)))).Entries
            : [];
    }

    /// <summary>
    /// Tells every session on this document to re-deliver, without changing the document.
    /// </summary>
    public void Touch(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        OnChanged(path);
    }

    /// <inheritdoc />
    public override void Forget(string path)
    {
        base.Forget(path);
        _identities.TryRemove(path, out _);
        _memories.TryRemove(path, out _);
    }

    /// <inheritdoc />
    protected override LineDocument Read(string text) => LineDocument.Parse(text);

    /// <inheritdoc />
    protected override string TextOf(LineDocument body) => body.Text;


    /// <inheritdoc />
    protected override ParseOutcome<WardleyMap> Parse(LineDocument body) => WardleyParser.Outcome(body);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The edit may have added, removed or renamed elements, so the identities are carried across
    /// it by line (<see cref="WardleyIdentityMoves"/>): every element the edit kept keeps its id,
    /// whatever it did to its key, and only now are they written. A map opened and never edited
    /// reaches none of this (Requirement 4.3). A sidecar that could not be written is the save's
    /// warning: the map landed, and the ids it hands out will not survive a restart.
    /// </para>
    /// <para>
    /// <b>A text this store held before has the identities it had then</b>, which is how an undo
    /// and a redo restore the ids of a removal, a rename or an add (findings B9 and B10): the
    /// restore writes the whole earlier text, and the line comparison alone would take a line it
    /// puts back for a new one.
    /// </para>
    /// </remarks>
    protected override Task<string> SavedAsync(string path, DocumentEntry<LineDocument, WardleyMap> entry, CancellationToken cancellationToken)
    {
        var text = entry.Document.Text;
        var memory = _memories.GetOrAdd(path, _ => new Memory());
        var before = _identities.TryGetValue(path, out var held) ? held : null;

        IReadOnlyList<WardleyIdentityEntry> carried;
        if (memory.Recall(text) is { } recalled)
        {
            carried = WardleyIdentities.Reconcile(entry.Model, recalled);
        }
        else if (before is not null)
        {
            carried = WardleyIdentityMoves.Follow(before.Model, before.Text, before.Entries, entry.Model, text);
        }
        else
        {
            carried = WardleyIdentities.Reconcile(entry.Model, _sidecar.Read(path));
        }

        if (before is not null)
        {
            memory.Keep(before.Text, before.Entries);
        }

        memory.Keep(text, carried);
        _identities[path] = new Held(text, entry.Model, carried);
        return _sidecar.WriteAsync(path, carried, cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Reconciled against what this process already assigned rather than against the sidecar
    /// alone: an element that survived an external edit keeps the id its connections are
    /// already holding, so a `git pull` does not silently reset every selection. Another program's
    /// edit can only be read by order, so the n-th holder of a kind and key takes the n-th id
    /// (ruling 7).
    /// </para>
    /// <para>
    /// <b>An undo arrives here too</b>: the restore writes the whole earlier text and reloads it.
    /// A text this store held before therefore takes the identities it had then, as a save does.
    /// </para>
    /// </remarks>
    protected override void Installed(string path)
    {
        if (Get(path) is not { } entry)
        {
            return;
        }

        var text = entry.Document.Text;
        var memory = _memories.GetOrAdd(path, _ => new Memory());
        var held = _identities.TryGetValue(path, out var current) ? current : null;
        var known = memory.Recall(text) ?? held?.Entries ?? [];
        var reconciled = WardleyIdentities.Reconcile(entry.Model, known);

        if (held is not null)
        {
            memory.Keep(held.Text, held.Entries);
        }

        memory.Keep(text, reconciled);
        _identities[path] = new Held(text, entry.Model, reconciled);
    }

    /// <summary>The identities held for a map, with the text and the map they are the identities of.</summary>
    private sealed record Held(string Text, WardleyMap Model, IReadOnlyList<WardleyIdentityEntry> Entries);

    /// <summary>The identities of the last texts of one map, the oldest forgotten first.</summary>
    private sealed class Memory
    {
        private readonly Lock _lock = new();
        private readonly Dictionary<string, IReadOnlyList<WardleyIdentityEntry>> _byText = new(StringComparer.Ordinal);
        private readonly Queue<string> _order = new();

        public IReadOnlyList<WardleyIdentityEntry>? Recall(string text)
        {
            lock (_lock)
            {
                return _byText.GetValueOrDefault(text);
            }
        }

        public void Keep(string text, IReadOnlyList<WardleyIdentityEntry> entries)
        {
            lock (_lock)
            {
                if (!_byText.ContainsKey(text))
                {
                    _order.Enqueue(text);
                }

                _byText[text] = entries;
                while (_order.Count > Remembered)
                {
                    _byText.Remove(_order.Dequeue());
                }
            }
        }
    }
}
