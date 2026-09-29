using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoopDiagram.Tests;

/// <summary>
/// What this module's own source files must be, as files, regardless of what they say.
/// </summary>
/// <remarks>
/// <para>
/// <b>Found by a defect this guard would have caught.</b> A raw NUL byte was written into a
/// string literal in <c>CausalLoopValidator.cs</c> — as the separator for a rotation-independent
/// cycle signature, where <c>"\0"</c> was meant and a literal zero byte was produced instead. It
/// compiled, it behaved correctly, every test passed, and the format checker was satisfied.
/// </para>
/// <para>
/// What it broke was everything that reads source as text. Git classified the file as binary in
/// the index — <c>git ls-files --eol</c> reported <c>i/-text</c> — so a diff on it is opaque in
/// review, and <c>grep</c> answered "Binary file matches" rather than the line, which is how it
/// was noticed at all: a search for a method name came back useless.
/// </para>
/// <para>
/// This is the shape of defect no behavioural test can reach, because the behaviour was never
/// wrong. So the guard is about the bytes.
/// </para>
/// <para>
/// <b>And then a second one walked past it, in the same file the first was found from.</b> A lone
/// carriage return — a <c>CR</c> not followed by <c>LF</c> — was spliced into
/// <c>CausalLoopSession.cs</c> by an editing script. Git's binary heuristic treats a lone CR
/// exactly as it treats a NUL, so that file was stored as binary too: <c>git ls-files --eol</c>
/// reported <c>i/-text</c> where all 84 of its siblings reported <c>i/lf</c>, and a thirty-line
/// addition committed as <c>@@ -1,234 +1,264 @@</c> — the whole file removed and re-added, with
/// the actual change invisible to a reviewer. <c>git add --renormalize</c> could not repair it,
/// because git was not mishandling the file: it was correctly reporting that the content was not
/// text.
/// </para>
/// <para>
/// The first version of this guard did not catch it, because it was written from the defect that
/// had been found rather than from the class that defect belongs to. It scanned for control
/// characters and deliberately excluded <c>CR</c>, <c>LF</c> and tab as the legitimate ones —
/// which is true of each byte alone and false of the sequences they form. So it now asserts the
/// sequences too.
/// </para>
/// </remarks>
public class ShapeOfSourcesTests
{
    private static string ModuleRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !Directory.Exists(IoPath.Combine(directory.FullName, "examples")))
            {
                directory = directory.Parent;
            }

            Assert.SkipWhen(directory is null, "The module root was not found from the test output.");
            return directory.FullName;
        }
    }

    private static IReadOnlyList<string> SourceFiles() =>
        [.. Directory.EnumerateFiles(ModuleRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))];

    /// <summary>
    /// No source file carries a control character outside tab, carriage return and line feed.
    /// </summary>
    /// <remarks>
    /// A control character inside a literal is almost always a mistake for its escape — a NUL for
    /// <c>\0</c>, a bell for <c>\a</c>, an escape for <c>\e</c> — and the escape is what the
    /// author meant in every case. Writing the byte instead compiles and works while making the
    /// file unreadable to git, grep, diff and review.
    /// </remarks>
    [Fact]
    public void NoSourceFile_CarriesARawControlCharacter()
    {
        // Arrange.
        var files = SourceFiles();

        // Assert.
        // The claim below is vacuously true of a module with no sources.
        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            var offset = Array.FindIndex(
                bytes,
                value => value < 0x20 && value is not ((byte)'\t' or (byte)'\r' or (byte)'\n'));

            Assert.True(
                offset < 0,
                $"{IoPath.GetFileName(path)} carries a raw control character (0x{(offset < 0 ? 0 : bytes[offset]):X2}) "
                + $"at byte {offset}. It was almost certainly meant as an escape. "
                + "Git stores a file like this as binary, so its diffs are opaque and grep cannot read it.");
        }
    }

    /// <summary>
    /// No source file carries a lone carriage return, which git reads as binary.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This repository's files are CRLF, so every <c>CR</c> must be followed by an <c>LF</c> and
    /// every <c>LF</c> preceded by a <c>CR</c>. A stray <c>CR</c> is what an editing script
    /// produces when it rewrites newlines in text that already had them — and it is invisible in
    /// every editor, because a lone CR renders as a line break just like the real ones.
    /// </para>
    /// <para>
    /// <b>Asserted on the bytes rather than by asking git.</b> Shelling out to
    /// <c>git ls-files --eol</c> would test the same thing one step later: it reports what git
    /// decided about a blob that has already been written. Reading the file catches the byte
    /// before it is ever staged, needs no repository present, and can name the offset.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoSourceFile_CarriesALoneCarriageReturn()
    {
        // Arrange.
        var files = SourceFiles();

        // Assert.
        // Vacuously true of a module with no sources, so the floor comes first.
        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);

            for (var index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] == (byte)'\r')
                {
                    Assert.True(
                        index + 1 < bytes.Length && bytes[index + 1] == (byte)'\n',
                        $"{IoPath.GetFileName(path)} carries a lone carriage return at byte {index}. "
                        + "Git reads that as binary exactly as it reads a NUL, so the file is stored "
                        + "unnormalised and every later change diffs as the whole file.");
                }
                else if (bytes[index] == (byte)'\n')
                {
                    Assert.True(
                        index > 0 && bytes[index - 1] == (byte)'\r',
                        $"{IoPath.GetFileName(path)} carries a bare line feed at byte {index}, among "
                        + "the CRLF endings of its siblings. Mixed endings make a diff unreadable "
                        + "even where git still calls the file text.");
                }
            }
        }
    }

    /// <summary>
    /// Every example body is text too, and for the same reason: a `.cld` is a document a reader
    /// opens and a reviewer diffs.
    /// </summary>
    [Fact]
    public void NoExampleDocument_CarriesARawControlCharacter()
    {
        // Arrange.
        var files = Directory.EnumerateFiles(
            IoPath.Combine(ModuleRoot, "examples"), "*.*", SearchOption.AllDirectories).ToArray();

        // Assert.
        Assert.NotEmpty(files);

        foreach (var path in files)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.DoesNotContain(
                bytes,
                value => value < 0x20 && value is not ((byte)'\t' or (byte)'\r' or (byte)'\n'));
        }
    }
}
