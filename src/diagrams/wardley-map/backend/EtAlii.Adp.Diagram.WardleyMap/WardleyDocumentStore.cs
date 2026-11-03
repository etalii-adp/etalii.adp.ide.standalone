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

    private readonly WardleyIdentityFiles _files = new(writer);

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
            ? _identities.GetOrAdd(path, key => new Held(entry.Document.Text, WardleyBody.Identify(entry.Document, _files.Read(key)))).Entries
            : [];
    }

    /// <inheritdoc />
    public void Remember(string path, string text, IReadOnlyList<WardleyIdentityEntry> identities)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(identities);
        _memories.GetOrAdd(path, _ => new Memory()).Offer(text, identities);
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
    protected override ParseOutcome<WardleyMap> Parse(LineDocument body) => WardleyBody.Read(body);

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// The edit may have added, removed or renamed elements, so the identities it carried are the
    /// ones its command handed over (<see cref="Remember"/>), which the library moved with their
    /// keys (<see cref="WardleyBodyEdit"/>): every element the edit kept keeps its id, whatever it
    /// did to its key, and only now are they written, where the map keeps them
    /// (<see cref="WardleyIdentityFiles"/>). A save no command handed identities to keeps each id
    /// its kind and key had. A map opened and never edited reaches none of this (Requirement 4.3).
    /// Identities that could not be written are the save's warning: the map landed, and the ids it
    /// hands out will not survive a restart.
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
            carried = WardleyBody.Identify(entry.Document, recalled);
        }
        else if (before is not null)
        {
            carried = WardleyBody.Identify(entry.Document, before.Entries);
        }
        else
        {
            carried = WardleyBody.Identify(entry.Document, _files.Read(path));
        }

        if (before is not null)
        {
            memory.Keep(before.Text, before.Entries);
        }

        memory.Keep(text, carried);
        _identities[path] = new Held(text, carried);
        return _files.WriteAsync(path, carried, cancellationToken);
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
        var reconciled = WardleyBody.Identify(entry.Document, known);

        if (held is not null)
        {
            memory.Keep(held.Text, held.Entries);
        }

        memory.Keep(text, reconciled);
        _identities[path] = new Held(text, reconciled);
    }

    /// <summary>The identities held for a map, with the text they are the identities of.</summary>
    private sealed record Held(string Text, IReadOnlyList<WardleyIdentityEntry> Entries);

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

        /// <summary>Keeps <paramref name="entries"/> for <paramref name="text"/> unless it has some already: a text held before keeps the identities it had then.</summary>
        public void Offer(string text, IReadOnlyList<WardleyIdentityEntry> entries)
        {
            lock (_lock)
            {
                if (_byText.ContainsKey(text)) return;
            }

            Keep(text, entries);
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
