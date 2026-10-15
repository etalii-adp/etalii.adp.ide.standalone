using System.Text;
using Path = System.IO.Path;

namespace EtAlii.Adp.Diagram.AnsibleStructure.Tests.Parity;

/// <summary>
/// What the parity transcript makes in the copy of a folder because git cannot hold it: an empty
/// folder, a link, a folder that is not there, a large binary, and a registration with stored
/// positions (ansible-structure-disl-fbl, Requirement 1.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Each is made in a temporary copy and recorded under its folder's fixed name</b>, so the
/// transcript holds no machine path, and nothing is made in a tree under <c>Parity/Folders/</c>
/// or under <c>Fixtures/</c>, which <c>ZeroWrites.Tests</c> holds.
/// </para>
/// <para>
/// A folder named here with no tree under <c>Parity/Folders/</c> is made from nothing. Every
/// making is described by one line, which the transcript writes beside the folder, so what was
/// made is part of what is reviewed.
/// </para>
/// <para>
/// <b>The binary and the registration could be committed and are made instead.</b> Eight
/// megabytes in the repository would say nothing a generated file does not, and a registration
/// committed beside Ansible files would claim the opposite of what the fixtures' readme says
/// about inferring a folder is a project.
/// </para>
/// </remarks>
internal static class AnsibleFindingFolders
{
    /// <summary>One thing made in a copy: the line the transcript writes for it, and the making.</summary>
    internal sealed record Making(string Line, Action<string> Make);

    /// <summary>The bytes of the large binary of finding B3: big enough that reading it to count it would show, and deterministic.</summary>
    private const int LargeBinaryLength = 8 * 1024 * 1024;

    /// <summary>What is made, by folder name, in the order it is made.</summary>
    internal static IReadOnlyDictionary<string, Making[]> Makings { get; } = new Dictionary<string, Making[]>(StringComparer.Ordinal)
    {
        // B2: a role folder holding nothing, a role whose only content is an empty meta/, and a
        // variable folder with no file in it. None reaches a plugin that is handed files only.
        ["b2-empty-folders"] =
        [
            Folder("roles/empty"),
            Folder("roles/metaonly/meta"),
            Folder("group_vars"),
        ],

        // B3: a binary a role ships. The reader counts it and never opens it.
        ["b3-large-binary"] =
        [
            new($"file roles/assets/files/blob.bin, {LargeBinaryLength} bytes of a repeating pattern", root =>
            {
                var bytes = new byte[LargeBinaryLength];
                for (var index = 0; index < bytes.Length; index++)
                {
                    bytes[index] = (byte)(index % 251);
                }

                Directory.CreateDirectory(Path.Combine(root, "roles", "assets", "files"));
                File.WriteAllBytes(Path.Combine(root, "roles", "assets", "files", "blob.bin"), bytes);
            }),
        ],

        // B5: the registered folder is not there at all.
        ["b5-not-there"] =
        [
            new("the folder is removed, registration and all", root => Directory.Delete(root, recursive: true)),
        ],

        // B9: positions stored for a playbook that is saved half way, and for one that is whole.
        ["b9-half-saved"] =
        [
            Registration("ansible/structure\nlayout:\n  playbook:half.yml: 100 200\n  playbook:other.yml: 300 400\n"),
        ],

        // B11: a playbook that is a link to a file, and a role that is a link to a folder.
        ["b11-links"] =
        [
            Link("linked.yml", "elsewhere/play.yml", directory: false),
            Link("roles/linked", "../elsewhere/role", directory: true),
        ],
    };

    /// <summary>The folders that are made from nothing: named here, with no tree under <c>Parity/Folders/</c>.</summary>
    internal static IEnumerable<string> MadeFromNothing(string folders) =>
        Makings.Keys.Where(name => !Directory.Exists(Path.Combine(folders, name)));

    /// <summary>The name a registration is written under, as the shipped examples name theirs.</summary>
    internal const string RegistrationName = ".adp";

    private static Making Folder(string relative) =>
        new($"empty folder {relative}", root => Directory.CreateDirectory(Path.Combine(root, relative)));

    private static Making Registration(string text) =>
        new($"registration {RegistrationName}: {TranscriptText.Quoted(text)}", root =>
            File.WriteAllText(Path.Combine(root, RegistrationName), text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)));

    /// <summary>
    /// A symbolic link, relative so the copy can lie anywhere. A platform that refuses one (Windows
    /// without Developer Mode) fails loudly here, rather than letting the transcript differ silently.
    /// </summary>
    private static Making Link(string relative, string target, bool directory) =>
        new($"{(directory ? "folder" : "file")} link {relative} -> {target}", root =>
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                if (directory)
                {
                    Directory.CreateSymbolicLink(path, target);
                }
                else
                {
                    File.CreateSymbolicLink(path, target);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"The parity transcript needs a symbolic link for finding B11 and this machine refused one ({exception.Message}). " +
                    "On Windows, turn on Developer Mode.",
                    exception);
            }
        });
}
