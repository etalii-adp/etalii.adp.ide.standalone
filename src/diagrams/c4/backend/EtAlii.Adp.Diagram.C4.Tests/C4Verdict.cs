using IoPath = System.IO.Path; // EtAlii.Adp.Path (the proto message) would otherwise shadow System.IO.Path here

namespace EtAlii.Adp.Diagram.C4.Tests;

/// <summary>
/// One recorded Structurizr <c>inspect</c> run, read from <c>Fixtures/verdicts</c>.
/// </summary>
/// <remarks>
/// <para>
/// This type knows nothing about ADP's rules. It parses <c>LEVEL | rule | message</c> and stops
/// there; the reconciliation test knows about both sides and is the only thing that does.
/// </para>
/// <para>
/// The verdicts exist so the interoperability guarantee can be checked without a JDK. Moving the
/// authority into a committed file is only worth anything if the file is provably still what
/// Structurizr says, which is what <c>C4InteropTests</c> checks when a CLI is present. Together
/// they make a stale baseline detectable rather than merely possible.
/// </para>
/// </remarks>
public sealed record C4Verdict(string CliVersion, IReadOnlyList<C4VerdictFinding> Findings)
{
    /// <summary>How to regenerate a verdict, named in every failure so nobody has to go looking.</summary>
    public const string RegenerationHint =
        "Regenerate the verdicts with the Structurizr CLI (`structurizr inspect -workspace <fixture>`) " +
        "and see C4InteropTests.EveryVerdict_IsStillWhatStructurizrSays.";

    private static string DirectoryPath => IoPath.Combine("Fixtures", "verdicts");

    /// <summary>The verdict recorded for <paramref name="fixture"/>, e.g. <c>big-bank-plc.dsl</c>.</summary>
    /// <remarks>
    /// Throws rather than returning null on a missing or unparseable file. A verdict that cannot
    /// be read is the reconciliation quietly guaranteeing nothing, which is the exact failure the
    /// baseline was introduced to prevent, so it must be loud.
    /// </remarks>
    public static C4Verdict Read(string fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var path = PathFor(fixture);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"No recorded Structurizr verdict for '{fixture}'. Every fixture in the corpus needs one. {RegenerationHint}",
                path);
        }

        return ReadFile(path);
    }

    /// <summary>
    /// The verdict in <paramref name="path"/>, wherever it happens to live.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Read"/> so a test can parse a deliberately broken file
    /// without moving the process's current directory - which is shared by every test running
    /// beside it, and moving it made two unrelated fixture tests fail at random.
    /// </remarks>
    internal static C4Verdict ReadFile(string path)
    {
        var lines = File.ReadAllLines(path);
        var version = ReadCliVersion(lines, path);
        var findings = new List<C4VerdictFinding>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            // Header lines and the blank line a text editor may leave at the end. Everything else
            // has to parse - silently skipping a line ADP does not understand would shrink the
            // comparison without anybody noticing.
            if (line.StartsWith('#') || line.Trim().Length == 0)
            {
                continue;
            }

            findings.Add(ParseFinding(line, path, index + 1));
        }

        return new C4Verdict(version, findings);
    }

    /// <summary>The distinct Structurizr rule ids this verdict reports.</summary>
    public IReadOnlySet<string> RuleIds =>
        Findings.Select(finding => finding.RuleId).ToHashSet(StringComparer.Ordinal);

    /// <summary>Whether a verdict has been recorded for <paramref name="fixture"/>.</summary>
    public static bool Exists(string fixture) => File.Exists(PathFor(fixture));

    private static string PathFor(string fixture) =>
        IoPath.Combine(DirectoryPath, IoPath.GetFileNameWithoutExtension(fixture) + ".inspect.txt");

    private static string ReadCliVersion(string[] lines, string path)
    {
        // `# structurizr-cli <version>, recorded <date>`. Requirement 3.3 asks which version
        // produced the file, so a verdict without one is not a verdict.
        var header = lines.Length > 0 ? lines[0] : "";
        const string prefix = "# structurizr-cli ";
        if (!header.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"'{path}' does not start with the `{prefix}<version>, recorded <date>` header, so there is no way to tell " +
                $"which Structurizr produced it. {RegenerationHint}");
        }

        var rest = header[prefix.Length..];
        var comma = rest.IndexOf(',', StringComparison.Ordinal);
        return (comma < 0 ? rest : rest[..comma]).Trim();
    }

    private static C4VerdictFinding ParseFinding(string line, string path, int number)
    {
        var parts = line.Split('|', 3);
        if (parts.Length < 3)
        {
            throw new FormatException(
                $"'{path}' line {number} is not `LEVEL | rule | message`: '{line}'. {RegenerationHint}");
        }

        var ruleId = parts[1].Trim();
        return ruleId.Length == 0
            ? throw new FormatException($"'{path}' line {number} names no rule: '{line}'. {RegenerationHint}")
            : new C4VerdictFinding(parts[0].Trim(), ruleId, parts[2].Trim());
    }
}

/// <summary>One line of a recorded verdict.</summary>
/// <remarks>
/// The message is kept for a failure to quote, never compared - ADP words its problems for the
/// person editing the document, and Structurizr words its for the person inspecting a workspace.
/// Requiring those to match would be requiring ADP to write Structurizr's sentences.
/// </remarks>
public sealed record C4VerdictFinding(string Level, string RuleId, string Message);
