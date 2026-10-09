using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace EtAlii.Adp.Documents;

/// <summary>
/// Which FILE a path named at one moment - its volume and NTFS file index - so a failed publish can
/// say whether the destination it was replacing is still the same file, was deleted, or was deleted
/// and created again while the publish ran.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why.</b> The only producers ever measured to make File.Replace fail with <c>0x80070497</c> are a
/// concurrent replace and a delete-and-recreate of the destination; no holder, whatever its sharing,
/// produces that code (a holder without Delete sharing gives <c>0x80070020</c>, one with it lets the
/// replace through). "Present, 132 bytes" cannot tell the original file from a recreated one. The file
/// index can: a recreated file is a new file record.
/// </para>
/// <para>
/// <b>NOT creation time, deliberately.</b> NTFS "tunneling" gives a file deleted and recreated under the
/// same name within about fifteen seconds its predecessor's creation time - so on exactly the shape this
/// exists to catch, creation time would answer "same file", wrong in the confident direction. Where
/// the index cannot be read (another platform, or any error) the answer is "unknown", never a guess.
/// </para>
/// <para>
/// <b>What it costs.</b> Read before every replace, because "before" must be taken before the outcome
/// is known: one attributes-only open - FILE_READ_ATTRIBUTES with every sharing flag, which cannot
/// block anybody or be blocked by a sharing mode - and one metadata call. Microseconds, not nothing.
/// </para>
/// </remarks>
internal readonly record struct DestinationIdentity(DestinationIdentity.State Kind, uint Volume, ulong Index, string? Why)
{
    internal enum State
    {
        Present,
        Absent,
        Unknown,
    }

    private string Id => $"{Volume:X8}:{Index:X16}";

    /// <summary>Reads the identity of whatever <paramref name="path"/> names now. Never throws.</summary>
    public static DestinationIdentity Of(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                return new DestinationIdentity(State.Unknown, 0, 0, "not readable on this platform");
            }

            return File.Exists(path) ? Read(path) : new DestinationIdentity(State.Absent, 0, 0, null);
        }
        catch (Exception exception)
        {
            return new DestinationIdentity(State.Unknown, 0, 0, $"{exception.GetType().Name} 0x{exception.HResult:X8}");
        }
    }

    /// <summary>The record's clause: what became of the destination between <paramref name="before"/> and now.</summary>
    public static string Describe(DestinationIdentity before, string path)
    {
        var now = Of(path);
        return (before.Kind, now.Kind) switch
        {
            (State.Unknown, _) => $"identity unknown ({before.Why})",
            (_, State.Unknown) => $"identity unknown ({now.Why})",
            (State.Present, State.Absent) => $"GONE since this publish began (it was file {before.Id})",
            (State.Present, State.Present) when before == now => $"same file as when this publish began ({now.Id})",
            (State.Present, State.Present) => $"RECREATED since this publish began (file {before.Id} is now {now.Id})",
            (State.Absent, State.Present) => $"none when this publish began, and one APPEARED since ({now.Id})",
            _ => "none when this publish began, and still none",
        };
    }

    [SupportedOSPlatform("windows")]
    private static DestinationIdentity Read(string path)
    {
        using var handle = CreateFileW(
            path, FileReadAttributes, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            // Gone between File.Exists and the open is an answer, not an error.
            return error is ErrorFileNotFound or ErrorPathNotFound
                ? new DestinationIdentity(State.Absent, 0, 0, null)
                : new DestinationIdentity(State.Unknown, 0, 0, $"open failed, error {error}");
        }

        if (!GetFileInformationByHandle(handle, out var information))
        {
            return new DestinationIdentity(State.Unknown, 0, 0, $"GetFileInformationByHandle failed, error {Marshal.GetLastPInvokeError()}");
        }

        return new DestinationIdentity(
            State.Present,
            information.VolumeSerialNumber,
            ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow,
            null);
    }

    private const uint FileReadAttributes = 0x80;
    private const uint FileShareAll = 0x7;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
        uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);
}
