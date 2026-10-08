using System.Text;
using System.Text.RegularExpressions;

namespace EtAlii.Adp.Specification.Fbl.Routing;

/// <summary>
/// The globs of FBL §10.1 and §12.1: relative, <c>/</c>-separated; <c>*</c> within one path segment,
/// <c>**</c> any number of segments, <c>?</c> one character, <c>[…]</c> a character class.
/// </summary>
public static class Glob
{
    /// <summary>Whether the file system is case-sensitive by the platform's convention: Windows and macOS are not.</summary>
    public static bool PlatformIgnoresCase => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public static bool IsMatch(string glob, string path, bool ignoreCase)
    {
        ArgumentNullException.ThrowIfNull(glob);
        ArgumentNullException.ThrowIfNull(path);
        var options = RegexOptions.CultureInvariant | (ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
        return Regex.IsMatch(path.Replace('\\', '/'), ToRegex(glob), options, TimeSpan.FromSeconds(1));
    }

    private static string ToRegex(string glob)
    {
        var builder = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*' when i + 1 < glob.Length && glob[i + 1] == '*':
                    var slash = i + 2 < glob.Length && glob[i + 2] == '/';
                    builder.Append(slash ? "(?:.*/)?" : ".*");
                    i += slash ? 2 : 1;
                    break;
                case '*':
                    builder.Append("[^/]*");
                    break;
                case '?':
                    builder.Append("[^/]");
                    break;
                case '[':
                    var close = glob.IndexOf(']', i + 2);
                    if (close < 0)
                    {
                        builder.Append("\\[");
                        break;
                    }
                    var set = glob[(i + 1)..close];
                    if (set.StartsWith('!')) set = "^" + set[1..];
                    builder.Append('[').Append(set.Replace("\\", "\\\\", StringComparison.Ordinal)).Append(']');
                    i = close;
                    break;
                default:
                    builder.Append(Regex.Escape(c.ToString()));
                    break;
            }
        }
        return builder.Append('$').ToString();
    }
}
