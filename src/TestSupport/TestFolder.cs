namespace EtAlii.Adp;

/// <summary>
/// Deletes a test's scratch folder, tolerating the short window in which Windows still
/// holds handles onto files inside it - a just-disposed FileSystemWatcher whose handle
/// the OS releases asynchronously, a stream closed a moment ago, an antivirus scan.
/// Retries briefly, then gives up silently: an orphaned folder under %TEMP% is
/// preferable to a red suite whose tests all passed. Compiled into every *.Tests
/// project via src/Directory.Build.targets.
/// </summary>
internal static class TestFolder
{
    public static void TryDelete(string path)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (attempt > 0)
            {
                Thread.Sleep(50 * attempt);
            }
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Still held; wait a little longer and try again.
            }
        }
    }
}
