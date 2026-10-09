using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.Knowledge.Tests;

/// <summary>The module's shipped example documents, and a place to copy one to for a test that changes it.</summary>
internal static class KnowledgeFiles
{
    private static readonly Lazy<string> _examples = new(() =>
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = IoPath.Combine(folder.FullName, "src", "designers", "knowledge", "examples");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("The Knowledge module's examples folder was not found above the test's output folder.");
    });

    /// <summary>The three formats a knowledge file comes in, by extension.</summary>
    public static TheoryData<string> Extensions => [".yaml", ".json", ".xml"];

    /// <summary>The session's own source file, for the one guard that has to read an order out of it.</summary>
    public static string SessionSource =>
        IoPath.GetFullPath(IoPath.Combine(_examples.Value, "..", "backend", "EtAlii.Adp.Designer.Knowledge", "KnowledgeSession.cs"));

    /// <summary>The path of a shipped example.</summary>
    private static string Example(string name) => IoPath.Combine(_examples.Value, name);

    /// <summary>The bytes of a shipped example.</summary>
    public static byte[] ExampleBytes(string name) => File.ReadAllBytes(Example(name));

    /// <summary>A copy of a shipped example in <paramref name="folder"/>, under its own name.</summary>
    public static string CopyExample(string name, string folder)
    {
        var path = IoPath.Combine(folder, name);
        File.Copy(Example(name), path);
        return path;
    }
}
