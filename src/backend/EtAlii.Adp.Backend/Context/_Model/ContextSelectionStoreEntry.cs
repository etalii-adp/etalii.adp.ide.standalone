using System.Threading.Channels;

namespace EtAlii.Adp.Backend.Context;

internal sealed class ContextSelectionStoreEntry
{
    public object Gate { get; } = new();
    public ChannelWriter<ContextMessage>? Writer { get; set; }
    public ContextSelectionRecord? Record { get; set; }
    public ContextRediscovery? Rediscover { get; set; }
    public Timer? IdleTimer { get; set; }

    /// <summary>
    /// The project root's actions, given on Register and carried on every "nothing
    /// selected" message. Computed once per Watch: they depend only on the root existing
    /// and on the discovered diagram types, both stable for a connection's lifetime.
    /// </summary>
    public IReadOnlyList<ContextActionGroupDefinition>? RootActions { get; set; }

    /// <summary>The project this connection belongs to; a project-actions push reaches only matching entries.</summary>
    public string RootPath { get; set; } = "";

    /// <summary>The project actions last sent, held so a re-registration re-sends them.</summary>
    public IReadOnlyList<ContextActionGroupDefinition>? ProjectActions { get; set; }
}
