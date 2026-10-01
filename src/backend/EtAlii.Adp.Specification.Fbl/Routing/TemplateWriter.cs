using System.Text;
using System.Text.RegularExpressions;
using EtAlii.Adp.Specification.Fbl.Documents;
using EtAlii.Adp.Specification.Fbl.Plugins;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>
/// New bodies from a binding's template (FBL §13): <c>template.byOrigin[origin]</c> before
/// <c>template.text</c>, else the plugin's <c>template</c> operation; the four placeholders replaced
/// and nothing else; and never an existing file overwritten.
/// </summary>
public static partial class TemplateWriter
{
    [GeneratedRegex(@"\{(name|base|key|newid:[A-Za-z_][A-Za-z0-9_-]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex Placeholder();

    /// <summary>
    /// The text of a new body named <paramref name="fileName"/>, or null when the binding has no
    /// template and no plugin was given to make one. <paramref name="newId"/> makes an id by DISL's
    /// strategy for a rule's type (<c>{newid:&lt;rule&gt;}</c>).
    /// </summary>
    public static byte[]? Produce(FblBinding binding, string? origin, string fileName, Func<string, string> newId, IPersistencePlugin? plugin = null)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(newId);
        var text = binding.Template is { } template
            ? origin is not null && template.ByOrigin.TryGetValue(origin, out var byOrigin) ? byOrigin : template.Text
            : null;
        if (text is not null) return Encoding.UTF8.GetBytes(Replace(text, fileName, newId));
        if (binding.Plugin is not null && plugin is not null) return plugin.Template(new PluginTemplateRequest(Path.GetFileName(fileName), Placeholders(fileName)));
        return null;
    }

    /// <summary>Replaces the four placeholder forms of FBL §13 and leaves every other brace literal.</summary>
    public static string Replace(string template, string fileName, Func<string, string> newId)
    {
        ArgumentNullException.ThrowIfNull(template);
        var values = Placeholders(fileName);
        return Placeholder().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            return name.StartsWith("newid:", StringComparison.Ordinal) ? newId(name[6..]) : values[name];
        });
    }

    /// <summary>The values of <c>{name}</c>, <c>{base}</c> and <c>{key}</c> for a new file.</summary>
    public static IReadOnlyDictionary<string, string> Placeholders(string fileName)
    {
        var name = Path.GetFileName(fileName);
        var baseName = Path.GetFileNameWithoutExtension(name);
        return new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = name, ["base"] = baseName, ["key"] = Key(baseName) };
    }

    /// <summary>
    /// <c>{key}</c> (FBL §13): every character outside ASCII letters, digits and <c>_</c> replaced by
    /// <c>_</c>, leading and trailing <c>_</c> removed, <c>_</c> prefixed when it then starts with a
    /// digit, and <c>untitled</c> when nothing is left.
    /// </summary>
    public static string Key(string baseName)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        var builder = new StringBuilder(baseName.Length);
        foreach (var c in baseName) builder.Append(char.IsAsciiLetterOrDigit(c) || c == '_' ? c : '_');
        var key = builder.ToString().Trim('_');
        if (key.Length == 0) return "untitled";
        return char.IsAsciiDigit(key[0]) ? "_" + key : key;
    }

    /// <summary>Writes a new body; an existing file is never overwritten (<see cref="FileMode.CreateNew"/> throws <see cref="IOException"/>).</summary>
    public static async Task CreateAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
    }
}
