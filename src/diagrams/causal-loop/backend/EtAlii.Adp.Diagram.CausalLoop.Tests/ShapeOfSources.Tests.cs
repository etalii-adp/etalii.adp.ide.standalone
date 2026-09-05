using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.CausalLoop.Tests;

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
