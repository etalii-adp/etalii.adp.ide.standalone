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
    private OpenBody(byte[] bytes, FblBinding binding, FblOptions options) : base(bytes)
    {
        Binding = binding;
        Options = options;
        Reading = BodyReading.Read(bytes, binding, options);
    }

    public FblBinding Binding { get; }

    public FblOptions Options { get; }

    public FblModel Model => Reading.ToModel();

    /// <summary>An unreadable body (FBL §7.5) and a read-only binding (FBL §3.4) refuse every change and are never saved.</summary>
    public bool IsReadOnly => Reading.Unreadable is not null || Binding.ReadOnly is not null;

    internal BodyReading Reading { get; private set; }

    public static OpenBody Open(byte[] bytes, FblBinding binding, FblOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(binding);
        return new OpenBody(bytes, binding, options ?? new FblOptions());
    }

    /// <summary>Plans <paramref name="change"/> against the current bytes without applying it.</summary>
    public PlanResult Plan(ModelChange change) => EditPlanner.Plan(Reading, change);

    /// <summary>Plans and applies <paramref name="change"/>: the planned edit, or the refusal with nothing written.</summary>
    public PlanResult Change(ModelChange change)
    {
        var result = Plan(change);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    /// <summary>
    /// Hands the body's bytes to <paramref name="write"/>, the host's atomic writer (FBL §6.6), which in
    /// this repository is <c>AdpFileWriter.Save</c>. The library writes no file itself: it references no
    /// project of the solution, and a second temp-then-move beside the central one is what the
    /// repository's file-access guard refuses.
    /// </summary>
    public void Save(Action<byte[]> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (Reading.Unreadable is not null) throw new InvalidOperationException("An unreadable body is never written (FBL §7.5).");
        if (Binding.ReadOnly is not null) throw new InvalidOperationException("A read-only body is never written (FBL §3.4).");
        write(Bytes);
    }

    protected override void Reread() => Reading = BodyReading.Read(Bytes, Binding, Options);
}
