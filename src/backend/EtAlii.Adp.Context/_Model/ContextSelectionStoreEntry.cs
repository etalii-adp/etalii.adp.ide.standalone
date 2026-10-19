using System.Threading.Channels;
using EtAlii.Adp.Context.Wire;
using EtAlii.Adp.Documents.Wire;

namespace EtAlii.Adp.Context;

internal sealed class ContextSelectionStoreEntry
{
    public object Gate { get; } = new();
    public ChannelWriter<ContextMessage>? Writer { get; set; }
    public ContextSelectionRecord? Record { get; set; }
    public ContextRediscovery? Rediscover { get; set; }

    /// <summary>
    /// The project root's actions, given on Register and carried on every "nothing
    /// selected" message. Computed once per Watch: they depend only on the root existing
    /// and on the discovered diagram types, both stable for a connection's lifetime.
    /// </summary>
    public IReadOnlyList<ContextActionGroupDefinition>? RootActions { get; set; }

    /// <summary>The project this connection belongs to; a project-actions push reaches only matching entries.</summary>
    public ProjectRoot Root { get; set; }

    /// <summary>
    /// The problems this connection was last sent, on its baseline or by a push. A push of the same
    /// set again is not sent: the connection already has it.
    /// </summary>
    public ProjectProblems? LastProblems { get; set; }
}
