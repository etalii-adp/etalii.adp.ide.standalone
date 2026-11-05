namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>A temporary folder holding a copy of one document, removed afterwards.</summary>
internal sealed class SupplyChainTestFolder : IDisposable
{
    private readonly TestFolder _folder = TestFolder.Create("EtAlii.Adp.SupplyChainTests");

    public SupplyChainTestFolder(string source, string name)
    {
        Folder = _folder.Path;
        Body = Path.Combine(Folder, name);
        File.Copy(source, Body);
    }

    public string Folder { get; }

    public string Body { get; }

    public void Dispose() => _folder.Dispose();
}
