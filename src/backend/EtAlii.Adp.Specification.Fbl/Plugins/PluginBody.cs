using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.History;
using EtAlii.Adp.Specification.Fbl.Planning;

namespace EtAlii.Adp.Specification.Fbl.Plugins;

/// <summary>
/// A file body read by a persistence plugin (FBL §11.3): the plugin reads and plans, and this host
/// side applies the splices, keeps the history, checks drift and saves, exactly as for a declared
/// body. Without the plugin the body opens read-only with DISL's <c>std.pluginMissing</c> (FBL §15.1).
/// </summary>
public sealed class PluginBody : SplicedFile
{
    private readonly IPersistencePlugin? _plugin;
    private PluginReadResult _last;

    private PluginBody(byte[] bytes, FblBinding binding, IPersistencePlugin? plugin, string fileName) : base(bytes)
    {
        Binding = binding;
        FileName = fileName;
        _plugin = plugin is not null && plugin.Id == binding.Plugin!.Plugin ? plugin : null;
        _last = ReadNow();
    }

    private FblBinding Binding { get; }

    private string FileName { get; }

    public FblModel Model => new(_last.Elements, _last.Findings, _last.Unreadable);

    /// <summary>Read-only without the plugin, when the plugin reports the body unreadable, or when the binding is read-only.</summary>
    public bool IsReadOnly => _plugin is null || _last.Unreadable || Binding.ReadOnly is not null;

    /// <summary>
    /// Opens a body whose binding names a plugin. <paramref name="plugin"/> is the one the caller has
    /// installed, or null; a plugin with another id counts as missing.
    /// </summary>
    public static PluginBody Open(byte[] bytes, FblBinding binding, IPersistencePlugin? plugin, string fileName = "body")
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(binding);
        if (binding.Plugin is null) throw new ArgumentException($"The binding '{binding.Name}' is read by its declared rules, not by a plugin.", nameof(binding));
        return new PluginBody(bytes, binding, plugin, fileName);
    }

    public PlanResult Plan(ModelChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (_plugin is null) return new PlanResult.Refused(MissingReason);
        if (_last.Unreadable) return new PlanResult.Refused("The file could not be read, so it is never written.");
        if (Binding.ReadOnly is { } reason) return new PlanResult.Refused(reason.Length > 0 ? reason : "This file is read-only.");
        if (change is ModelChange.Save) return new PlanResult.Planned(new Edit([]));
        var result = _plugin.Plan(new PluginPlanRequest([new PluginFile("", Bytes)], _last, change, Binding.Plugin!.Args));
        return result switch
        {
            PluginPlanResult.Refused refused => new PlanResult.Refused(refused.Reason),
            PluginPlanResult.Planned planned when planned.Splices.Any(s => s.File.Length > 0) =>
                new PlanResult.Refused("The plugin planned a change to another file than the body."),
            PluginPlanResult.Planned planned => new PlanResult.Planned(new Edit(Order(planned.Splices.Select(s => s.Splice)))),
            _ => throw new InvalidOperationException("The plugin returned no plan."),
        };
    }

    public PlanResult Change(ModelChange change)
    {
        var result = Plan(change);
        if (result is PlanResult.Planned planned) Apply(planned.Edit);
        return result;
    }

    /// <summary>Hands the body's bytes to the host's atomic writer, as <see cref="OpenBody.Save"/> does; never a read-only body.</summary>
    public void Save(Action<byte[]> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (IsReadOnly) throw new InvalidOperationException("A read-only body is never written.");
        write(Bytes);
    }

    private string MissingReason => $"The plugin '{Binding.Plugin!.Plugin}' that reads this file is not installed, so it is opened read-only.";

    private PluginReadResult ReadNow()
    {
        if (_plugin is null)
        {
            var finding = new Finding(FindingCodes.PluginMissing, FindingSeverity.Warning, MissingReason, new SourceLocation(FileName, 1, 1, 0));
            return new PluginReadResult([], [finding], false);
        }
        return _plugin.Read(new PluginReadRequest([new PluginFile("", Bytes)], Binding.Plugin!.Args));
    }

    /// <summary>A plugin's splices in body order, as FBL §6.5 applies them; overlapping splices are the plugin's error.</summary>
    private static IReadOnlyList<Splice> Order(IEnumerable<Splice> splices)
    {
        var ordered = splices.Select((s, i) => (s, i)).OrderBy(p => p.s.Start).ThenBy(p => p.i).Select(p => p.s).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Start < ordered[i - 1].End) throw new InvalidOperationException($"The plugin planned overlapping splices: {ordered[i - 1]} and {ordered[i]}.");
        }
        return ordered;
    }

    protected override void Reread() => _last = ReadNow();
}
