namespace EtAlii.Adp.Diagram.Sankey.Tests;

/// <summary>A temporary folder holding a copy of one document, removed afterwards.</summary>
internal sealed class SankeyTestFolder : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.SankeyTests");

    public SankeyTestFolder(string source, string name)
    {
        Folder = _folder.Path;
        Body = Path.Combine(Folder, name);
        File.Copy(source, Body);
    }

    public string Folder { get; }

    public string Body { get; }

    public void Dispose() => _folder.Dispose();
}
