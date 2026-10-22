using System.Text;

namespace EtAlii.Adp.Specification.Fbl.Bullets;

/// <summary>Columns as the <c>bullets</c> family counts them (FBL 0.7 §4.8): from 0, a tab four columns wherever it stands.</summary>
internal static class Columns
{
    /// <summary>The columns of a run of spaces and tabs.</summary>
    public static int Of(string whitespace) => whitespace.Sum(character => character == '\t' ? 4 : 1);

    /// <summary>The columns of the whitespace <paramref name="line"/> starts with.</summary>
    public static int Leading(string line) => Of(line[..(line.Length - line.TrimStart(' ', '\t').Length)]);

    /// <summary>
    /// <paramref name="line"/> with the whitespace before <paramref name="column"/> taken off: a line indented
    /// less loses all of it, and a tab the column cuts into is replaced by the spaces that remain of it.
    /// </summary>
    public static string Without(string line, int column)
    {
        var at = 0;
        var index = 0;
        while (index < line.Length && line[index] is ' ' or '\t' && at < column)
        {
            var width = line[index] == '\t' ? 4 : 1;
            if (at + width > column) return new string(' ', at + width - column) + line[(index + 1)..];
            at += width;
            index++;
        }
        return line[index..];
    }

    /// <summary><paramref name="line"/> shifted by <paramref name="shift"/> columns: right by writing spaces before it, left by <see cref="Without"/>.</summary>
    public static string Shift(string line, int shift) => shift >= 0 ? new string(' ', shift) + line : Without(line, -shift);

    /// <summary>The lines of <paramref name="text"/> with their endings, each ending kept with its line.</summary>
    public static IEnumerable<(string Line, string Ending)> Lines(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            var ending = text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? "\r\n" : text[i].ToString();
            yield return (text[start..i], ending);
            i += ending.Length - 1;
            start = i + 1;
        }
        if (start < text.Length) yield return (text[start..], "");
    }

    /// <summary>Text written as lines, each after a line ending: <paramref name="value"/>'s lines, each indented with spaces to <paramref name="column"/>, an empty line written empty.</summary>
    public static string Indented(string value, int column, string newline)
    {
        var builder = new StringBuilder();
        foreach (var line in value.Split('\n')) builder.Append(newline).Append(line.Length == 0 ? "" : new string(' ', column) + line);
        return builder.ToString();
    }
}
