using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One <see cref="AnsibleProject"/> per registered folder, kept true as the tree changes
/// (Requirement 3.5) - the module's only reader, so two people looking at one project cannot see
/// different structures. The watching, the claims and the settling are the shared
/// <see cref="WatchedFolderStore{TSnapshot}"/>'s; this says how a folder is read.
/// </summary>
/// <remarks>
/// A registered folder is one Ansible project, the burst timer collapses a multi-file save into a
/// single re-read, and the walk is milliseconds, so every settled burst re-reads the whole project.
/// If a project ever appears where that is not true, the place to fix it is
/// <see cref="AnsibleProjectReader"/>.
/// </remarks>
public sealed class AnsibleProjectStore(AnsibleProjectReader reader, TimeSpan? settleDelay = null)
    : WatchedFolderStore<AnsibleProject>(settleDelay ?? DefaultSettleDelay), IAnsibleProjectStore
{
    /// <summary>How long a burst of changes may keep growing before one re-read answers it all.</summary>
    private static readonly TimeSpan DefaultSettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly AnsibleProjectReader _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>A store over the module's own reader; the shape the host and most tests want.</summary>
    public AnsibleProjectStore()
        : this(new AnsibleProjectReader())
    {
    }

    /// <inheritdoc />
    protected override AnsibleProject Read(string folder) => _reader.Read(folder);
}
