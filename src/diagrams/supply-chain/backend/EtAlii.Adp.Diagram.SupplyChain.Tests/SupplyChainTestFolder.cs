namespace EtAlii.Adp.Diagram.SupplyChain.Tests;

/// <summary>A temporary folder holding a copy of one document, removed afterwards.</summary>
internal sealed class SupplyChainTestFolder : IDisposable
{
    public SupplyChainTestFolder(string source, string name)
    {
        Folder = Path.Combine(Path.GetTempPath(), "EtAlii.Adp.SupplyChainTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        Body = Path.Combine(Folder, name);
        File.Copy(source, Body);
    }

    public string Folder { get; }

    public string Body { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A temp folder left behind is not a test failure.
        }
    }
}
