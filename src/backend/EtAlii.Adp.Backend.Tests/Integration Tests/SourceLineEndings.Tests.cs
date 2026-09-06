using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Backend.Tests;

/// <summary>
/// No C# source file under <c>src</c> contains a carriage return that is not part of a
/// CRLF pair. A stray CR makes git classify the file as binary (<c>-text</c>), after which
/// nothing normalizes it: diffs become whole-file rewrites, blame breaks, and the house
/// CRLF policy silently stops applying to that file.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists as a test.</b> During backend-project-decomposition task 3, a script
/// inserting using directives matched across a line boundary and split a CRLF pair, landing
/// three <c>-text</c> files on develop (<c>ContextSelectionResolver.cs</c>, its tests, and
/// <c>ProjectService.cs</c>) - found only because task 4's diff was thirty times its
/// expected size. <c>git ls-files --eol</c> shows the damage but no gate reads it; this
/// guard puts the check inside <c>dotnet test</c>, where it runs on every landing.
/// </para>
/// <para>
/// <b>Scope.</b> Tracked-shaped source only: every <c>*.cs</c> under <c>src</c>, skipping
/// <c>obj</c>, <c>bin</c> and <c>node_modules</c>. Fixture documents whose bytes are the
/// test subject are deliberately <c>-text</c> by extension in <c>.gitattributes</c> - none
/// of those are C# files, so this guard cannot collide with them.
/// </para>
/// <para>
/// <b>Seen to fail</b> (backend-project-decomposition task 4): a CR spliced into one file
/// produced exactly one failure naming that file and its byte offset; reverting restored
/// green.
/// </para>
/// </remarks>
public class SourceLineEndingsTests
{
    [Fact]
    public void NoSourceFileCarriesAStrayCarriageReturn()
    {
        var src = SourceRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var relative = IoPath.GetRelativePath(src, file);
            if (relative.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}")
                || relative.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}")
                || relative.Contains("node_modules"))
            {
                continue;
            }

            var bytes = File.ReadAllBytes(file);
            for (var i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == (byte)'\r' && (i + 1 == bytes.Length || bytes[i + 1] != (byte)'\n'))
                {
                    offenders.Add($"{relative}: stray CR at byte {i}");
                    break;
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>The src folder, found by walking up - the family's shared idiom.</summary>
    private static string SourceRoot()
    {
        var directory = AppContext.BaseDirectory;
        for (var depth = 0; depth < 12; depth++)
        {
            var candidate = IoPath.Combine(directory, "src", "backend");
            if (File.Exists(IoPath.Combine(candidate, "EtAlii.Adp.slnx")))
            {
                return IoPath.Combine(directory, "src");
            }

            directory = IoPath.GetDirectoryName(directory) ?? throw new InvalidOperationException("src was not found above the test assembly.");
        }

        throw new InvalidOperationException("src was not found above the test assembly.");
    }
}
