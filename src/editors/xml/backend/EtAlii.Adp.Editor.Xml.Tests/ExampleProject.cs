using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests;

/// <summary>
/// The module's examples copied into a project folder of their own, so a test may change a file
/// without touching the source tree, and asked of the analyser as the editor service asks it.
/// </summary>
internal sealed class ExampleProject : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("xml-examples");

    public ExampleProject(XmlAnalyzer? analyzer = null)
    {
        Analyzer = analyzer ?? new XmlAnalyzer();
        Directory.CreateDirectory(Root);
        foreach (var file in Directory.EnumerateFiles(RepositoryPaths.Under("src", "editors", "xml", "examples")))
        {
            File.Copy(file, Path.Combine(Root, Path.GetFileName(file)));
        }
    }

    public string Root => _folder.Combine("project");

    private XmlAnalyzer Analyzer { get; }

    public string PathOf(string name) => Path.Combine(Root, name);

    public string Text(string name) => File.ReadAllText(PathOf(name)).ReplaceLineEndings("\n");

    /// <summary>Writes <paramref name="text"/> as <paramref name="name"/> and moves its last-write time on, so a change is seen whatever the clock's grain.</summary>
    public void Write(string name, string text)
    {
        var path = PathOf(name);
        var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.UtcNow;
        File.WriteAllText(path, text);
        File.SetLastWriteTimeUtc(path, before.AddSeconds(5));
    }

    public Task<TextAnalysis> AnalyzeAsync(string name, string? text = null) =>
        Analyzer.AnalyzeAsync(Root, PathOf(name), text ?? Text(name), TestContext.Current.CancellationToken);

    /// <summary>Asks for completion where <paramref name="text"/> holds a <c>|</c>, which is taken out first.</summary>
    public Task<IReadOnlyList<TextCompletion>> CompleteAsync(string name, string text, bool isExplicit = false)
    {
        var offset = text.IndexOf('|', StringComparison.Ordinal);
        Assert.True(offset >= 0, "the text marks no caret");
        return Analyzer.CompleteAsync(Root, PathOf(name), text.Remove(offset, 1), offset, isExplicit, TestContext.Current.CancellationToken);
    }

    public void Dispose() => _folder.Dispose();
}
