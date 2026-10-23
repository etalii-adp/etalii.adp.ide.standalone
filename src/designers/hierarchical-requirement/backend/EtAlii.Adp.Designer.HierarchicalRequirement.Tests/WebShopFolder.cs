using IoPath = System.IO.Path;

namespace EtAlii.Adp.Designer.HierarchicalRequirement.Tests;

/// <summary>A copy of the module's example, the web shop, with its registration, in a folder of its own that is removed afterwards.</summary>
internal sealed class WebShopFolder : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("adp-requirement-page-").FullName;

    public WebShopFolder()
    {
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.md"), Body);
        File.Copy(IoPath.Combine(HierarchicalRequirementFiles.ExamplesFolder, "web-shop.adp"), Registration);
    }

    public string Body => IoPath.Combine(_root, "web-shop.md");

    public string Registration => IoPath.Combine(_root, "web-shop.adp");

    /// <summary>The body's text as it is now.</summary>
    public string Text => File.ReadAllText(Body);

    /// <summary>Writes the body as another program does: the whole file, over what is there.</summary>
    public void WriteAsAnAgent(Func<string, string> change) => File.WriteAllText(Body, change(Text));

    /// <summary>
    /// Replaces <paramref name="path"/> with <paramref name="bytes"/> in one step, as an editor saves: written beside it,
    /// then moved over it, so no reader sees the file half-written.
    /// </summary>
    public static async Task ReplaceAsync(string path, byte[] bytes)
    {
        var beside = path + ".saving";
        await File.WriteAllBytesAsync(beside, bytes);
        File.Move(beside, path, overwrite: true);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A watcher may still be letting go; the temp folder is the system's to clear.
        }
    }
}
