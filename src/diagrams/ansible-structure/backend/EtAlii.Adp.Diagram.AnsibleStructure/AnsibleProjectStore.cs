using EtAlii.Adp.Documents;

namespace EtAlii.Adp.Diagram.AnsibleStructure;

/// <summary>
/// One <see cref="AnsibleProject"/> per registered folder, kept true as the tree changes
/// (Requirement 3.5) - the module's only reader, so two people looking at one project cannot see
/// different structures. The watching, the claims and the settling are the shared
/// <see cref="WatchedFolderStore{TSnapshot}"/>'s; this says how a folder is read: its files selected
/// through the binding and read as <see cref="AnsibleFolderPlugin"/> reads them
/// (<see cref="AnsibleProjectReader.Read(string)"/>). The library's folder body takes over the watching
/// later (ansible-structure-disl-fbl, task 10).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it holds is the plugin's reading, and it is read only as the definition's model</b>
/// (ansible-structure-disl-fbl, Requirement 3.5): the session, the selection resolver and the property
/// provider hand it to <see cref="AnsibleDefinition.ModelOf"/>, which builds the model once per reading, and
/// draw, select and describe from that. It goes with the store in task 10.
/// </para>
/// <para>
/// A registered folder is one Ansible project, the burst timer collapses a multi-file save into a
/// single re-read, and the walk is milliseconds, so every settled burst re-reads the whole project.
/// If a project ever appears where that is not true, the place to fix it is
/// <see cref="AnsibleProjectReader"/>.
/// </para>
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
