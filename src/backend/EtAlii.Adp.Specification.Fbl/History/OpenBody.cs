using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Planning;
using EtAlii.Adp.Specification.Fbl.Rules;

namespace EtAlii.Adp.Specification.Fbl.History;

/// <summary>
/// One open body (FBL §9.1): its bytes, its reading through one binding, and the one history of its
/// edits. Re-reading after every edit, rather than patching the model, keeps one code path for what
/// a body means. Beside the reading it was opened with, it keeps one for every other origin and resource
/// it is viewed as (FBL 0.10 §9.1): each reading of the body is a view of it, an edit made in any of them is
/// planned against the one set of bytes and recorded in the one history, and every view reads it.
/// </summary>
public sealed class OpenBody : SplicedFile
{
    private OpenBody(byte[] bytes, FblBinding binding, FblOptions options) : base(bytes)
    {
        Binding = binding;
        Options = options;
        Reading = BodyReading.Read(bytes, binding, options);
    }

    private OpenBody(OpenBody body) : base(body.Bytes)
    {
        Binding = body.Binding;
        Options = body.Options;
        Reading = body.Reading;
    }

    // The readings of the views this body is asked for (FBL 0.10 §9.1), by origin and resource, read on first use
    // after each change of the bytes.
    private readonly Dictionary<(string? Origin, string? Resource), BodyReading> _views = [];

    private FblBinding Binding { get; }

    private FblOptions Options { get; }

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

    /// <summary>
    /// Another open body on this one's bytes as they are now, with a history of its own, without
    /// reading them again: the reading is a pure function of the bytes, the binding and the options,
    /// so the two share it until either changes, when that one reads its own new bytes.
    /// </summary>
    public OpenBody Fork() => new(this);

    /// <summary>Plans <paramref name="change"/> against the current bytes without applying it.</summary>
    /// <remarks>A reading shared by forks is planned against one at a time: planning fills its caches.</remarks>
    public PlanResult Plan(ModelChange change)
    {
        var reading = Reading;
        lock (reading)
        {
            return EditPlanner.Plan(reading, change);
        }
    }

    /// <summary>Plans and applies <paramref name="change"/>: the planned edit, or the refusal with nothing written.</summary>
    public PlanResult Change(ModelChange change)
    {
        var result = Plan(change);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    /// <summary>
    /// Plans and applies <paramref name="change"/> when <paramref name="current"/>, the file as it is now on disk or in
    /// another editor's buffer, is the bytes it is planned against (FBL 0.7 §7.2). When it is not, the edit is refused
    /// with <see cref="SplicedFile.DriftEdit"/>, nothing is written, and the body is read again from
    /// <paramref name="current"/> (FBL §7.3).
    /// </summary>
    public PlanResult Change(ModelChange change, byte[] current)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (current.AsSpan().SequenceEqual(Bytes)) return Change(change);
        Reload(current);
        return new PlanResult.Refused(DriftEdit);
    }

    /// <summary>
    /// Hands the body's bytes to <paramref name="write"/>, the host's atomic writer (FBL §6.6), which in
    /// this repository is the host's <c>IFileWriter</c>. The library writes no file itself: it references no
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

    /// <summary>
    /// The model of the reading of <paramref name="origin"/> that selects <paramref name="resource"/> (FBL 0.10 §8.2,
    /// §9.1), null for the first of the reading's capture: the view of this body a registration of that tool type
    /// shows. It is read from the bytes as they are now.
    /// </summary>
    public FblModel ModelOf(string? origin, string? resource) => View(origin, resource).ToModel();

    /// <summary>Plans <paramref name="change"/> in the view of <paramref name="origin"/> and <paramref name="resource"/>, without applying it.</summary>
    private PlanResult Plan(ModelChange change, string? origin, string? resource)
    {
        var reading = View(origin, resource);
        lock (reading)
        {
            return EditPlanner.Plan(reading, change);
        }
    }

    /// <summary>
    /// Plans <paramref name="change"/> in the view of <paramref name="origin"/> and <paramref name="resource"/> and applies
    /// it to the one body (FBL 0.10 §9.1): recorded in its one history, so an undo in any view takes it back, and read by
    /// every view.
    /// </summary>
    public PlanResult Change(ModelChange change, string? origin, string? resource)
    {
        var result = Plan(change, origin, resource);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    /// <summary>The reading of a view: the one this body was opened with when it is that view, else one read for it.</summary>
    private BodyReading View(string? origin, string? resource)
    {
        if (origin == Options.Origin && resource == Options.Resource) return Reading;
        lock (_views)
        {
            if (_views.TryGetValue((origin, resource), out var known)) return known;
            var options = Options.With(origin, resource);
            var reading = BodyReading.Read(Bytes, Binding, options);
            _views[(origin, resource)] = reading;
            return reading;
        }
    }

    protected override void Reread()
    {
        Reading = BodyReading.Read(Bytes, Binding, Options);
        lock (_views) _views.Clear();
    }
}
