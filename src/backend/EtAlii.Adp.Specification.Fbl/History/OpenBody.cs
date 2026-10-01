using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.History;

/// <summary>
/// One open body (FBL §9.1): its bytes, its reading through one binding, and the one history of its
/// edits. Re-reading after every edit, rather than patching the model, keeps one code path for what
/// a body means.
/// </summary>
public sealed class OpenBody : SplicedFile
{
    private BodyReading _reading;

    private OpenBody(byte[] bytes, FblBinding binding, FblOptions options) : base(bytes)
    {
        Binding = binding;
        Options = options;
        _reading = BodyReading.Read(bytes, binding, options);
    }

    public FblBinding Binding { get; }

    public FblOptions Options { get; }

    public FblModel Model => _reading.ToModel();

    /// <summary>An unreadable body (FBL §7.5) and a read-only binding (FBL §3.4) refuse every change and are never saved.</summary>
    public bool IsReadOnly => _reading.Unreadable is not null || Binding.ReadOnly is not null;

    internal BodyReading Reading => _reading;

    public static OpenBody Open(byte[] bytes, FblBinding binding, FblOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(binding);
        return new OpenBody(bytes, binding, options ?? new FblOptions());
    }

    /// <summary>Plans <paramref name="change"/> against the current bytes without applying it.</summary>
    public PlanResult Plan(ModelChange change) => EditPlanner.Plan(_reading, change);

    /// <summary>Plans and applies <paramref name="change"/>: the planned edit, or the refusal with nothing written.</summary>
    public PlanResult Change(ModelChange change)
    {
        var result = Plan(change);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    /// <summary>
    /// Writes the body atomically (FBL §6.6). This save has no per-destination turn, as the standalone
    /// host's <c>AdpFileWriter.Save</c> has; a host that adopts this library saves <see cref="SplicedFile.Bytes"/>
    /// through its own writer instead.
    /// </summary>
    public async Task SaveAsync(string path, CancellationToken cancellationToken)
    {
        if (_reading.Unreadable is not null) throw new InvalidOperationException("An unreadable body is never written (FBL §7.5).");
        if (Binding.ReadOnly is not null) throw new InvalidOperationException("A read-only body is never written (FBL §3.4).");
        await AtomicFile.WriteAsync(path, Bytes, cancellationToken).ConfigureAwait(false);
    }

    protected override void Reread() => _reading = BodyReading.Read(Bytes, Binding, Options);
}
