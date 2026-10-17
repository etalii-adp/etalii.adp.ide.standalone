using EtAlii.Adp.Specification.Fbl.History;

namespace EtAlii.Adp.Diagram.Modules;

/// <summary>
/// The open bodies of the last few texts a module read or wrote, by their bytes: forks no one edits,
/// only forked again.
/// </summary>
/// <remarks>
/// Reading is a pure function of the bytes, so a module that is asked for the same text in turn - by
/// its store, its validator, its menus and a command - forks the body it read lately rather than
/// reading it once more. Each fork is its own body, so an edit to one is never seen by another.
/// </remarks>
public sealed class RecentBodies
{
    private const int Capacity = 8;

    private readonly LinkedList<(int Hash, OpenBody Body)> _bodies = [];
    private readonly Lock _lock = new();

    /// <summary>The body remembered for <paramref name="bytes"/>, now the most recent; null when none is.</summary>
    public OpenBody? Find(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var hash = Hash(bytes);
        lock (_lock)
        {
            for (var node = _bodies.First; node is not null; node = node.Next)
            {
                if (node.Value.Hash != hash || !node.Value.Body.Bytes.AsSpan().SequenceEqual(bytes)) continue;
                _bodies.Remove(node);
                _bodies.AddFirst(node);
                return node.Value.Body;
            }
        }

        return null;
    }

    /// <summary>Remembers <paramref name="body"/> as the most recent, in place of one with the same bytes, forgetting the oldest beyond the few kept.</summary>
    public void Remember(OpenBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var hash = Hash(body.Bytes);
        lock (_lock)
        {
            for (var node = _bodies.First; node is not null; node = node.Next)
            {
                if (node.Value.Hash == hash && node.Value.Body.Bytes.AsSpan().SequenceEqual(body.Bytes))
                {
                    _bodies.Remove(node);
                    break;
                }
            }

            _bodies.AddFirst((hash, body));
            while (_bodies.Count > Capacity)
            {
                _bodies.RemoveLast();
            }
        }
    }

    private static int Hash(byte[] bytes)
    {
        var hash = new HashCode();
        hash.AddBytes(bytes);
        return hash.ToHashCode();
    }
}
