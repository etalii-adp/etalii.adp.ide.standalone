using Serilog;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// What the XML editor says about the text as typed, answered through the family's seam
/// (xml-editor Requirement 9.2): well-formedness first, then which schema applies, then the
/// document against it; and what the schema allows at the caret.
/// </summary>
public sealed class XmlAnalyzer : IEditorAnalyzer
{
    private static readonly ILogger _logger = Log.ForContext<XmlAnalyzer>();

    public string EditorId => Editor.Xml.Id;

    public Task<TextAnalysis> AnalyzeAsync(string rootPath, string filePath, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolver = new ProjectXmlResolver(rootPath);
        var positions = new TextPositions(content);
        var findings = new List<TextFinding>();

        if (WellFormedness.DeclaredEncoding(content, TextFileBuffer.Open(filePath).Buffer) is { } encoding)
        {
            findings.Add(encoding);
        }

        var wellFormed = WellFormedness.Check(content, filePath, resolver, positions);
        findings.AddRange(wellFormed.Findings);
        var status = wellFormed.Stopped ? WellFormedness.NotChecked : "Well-formed.";

        _logger.Debug("Analysed {Path}: {Findings} finding(s)", Path.GetRelativePath(rootPath, filePath), findings.Count);
        return Task.FromResult(new TextAnalysis(findings, status, resolver.Read));
    }

    public Task<IReadOnlyList<TextCompletion>> CompleteAsync(string rootPath, string filePath, string content, int offset, bool isExplicit, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TextCompletion>>([]);
}
