using Serilog;

namespace EtAlii.Adp.Editor.Xml;

/// <summary>
/// What the XML editor says about the text as typed, answered through the family's seam
/// (xml-editor Requirement 9.2): well-formedness first, then which schema applies, then the
/// document against it; and what the schema allows at the caret.
/// </summary>
public sealed class XmlAnalyzer : IEditorAnalyzer
{
    private const string NoSchema = "No schema";
    private const string DtdChecked = "DTD: checked, no suggestions.";

    private static readonly ILogger _logger = Log.ForContext<XmlAnalyzer>();

    private readonly SchemaCache _schemas;

    public XmlAnalyzer()
        : this(new SchemaCache())
    {
    }

    internal XmlAnalyzer(SchemaCache schemas)
    {
        _schemas = schemas;
    }

    public string EditorId => Editor.Xml.Id;

    public Task<TextAnalysis> AnalyzeAsync(ProjectPath file, string content, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = file.Root;
        var filePath = file.FullPath;

        var resolver = new ProjectXmlResolver(root);
        var positions = new TextPositions(content);
        var findings = new List<TextFinding>();
        var dependsOn = new List<string>();

        if (WellFormedness.DeclaredEncoding(content, TextFileBuffer.Open(filePath).Buffer) is { } encoding)
        {
            findings.Add(encoding);
        }

        var wellFormed = WellFormedness.Check(content, filePath, resolver, positions);
        findings.AddRange(wellFormed.Findings);

        var association = SchemaAssociation.Read(content, filePath, resolver, positions);
        findings.AddRange(association.Unsupported);

        var applied = Applied(root, filePath, content, association, resolver, findings, dependsOn);
        string status;
        if (wellFormed.Stopped)
        {
            // Requirement 6.5: no schema finding for a text that is not well-formed.
            status = association.NamesASchema ? $"{WellFormedness.NotChecked} Schema not checked." : WellFormedness.NotChecked;
        }
        else
        {
            status = applied.Status;
            if (applied.Validate)
            {
                findings.AddRange(SchemaValidation.Validate(content, filePath, resolver, positions, applied.Set?.Set));
            }
        }

        dependsOn.AddRange(resolver.Read);
        _logger.Debug("Analysed {Path}: {Findings} finding(s)", file.RelativePath, findings.Count);
        return Task.FromResult(new TextAnalysis(
            findings,
            status,
            dependsOn.Distinct(StringComparer.Ordinal).Where(path => !string.Equals(path, Path.GetFullPath(filePath), StringComparison.Ordinal)).ToArray()));
    }

    public Task<IReadOnlyList<TextCompletion>> CompleteAsync(ProjectPath file, string content, int offset, bool isExplicit, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var root = file.Root;
        var filePath = file.FullPath;

        // Only an XSD the document names offers anything: a DTD validates and offers nothing
        // (ruling A2), and with no schema the end tag is the client's (Requirement 7.8).
        var resolver = new ProjectXmlResolver(root);
        var association = SchemaAssociation.Read(content, filePath, resolver, new TextPositions(content));
        if (association.IsSchema || association.Xsd.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<TextCompletion>>([]);
        }

        var resolved = ResolvedXsd(association, filePath, resolver, [], []);
        var compiled = resolved.Count == 0 ? null : _schemas.Get(root, Roots(resolved));
        return compiled is not { Set: { } set, Problem: null, Version11: null }
            ? Task.FromResult<IReadOnlyList<TextCompletion>>([])
            : Task.FromResult(XmlCompletion.Complete(content, offset, isExplicit, set, filePath, resolver));
    }

    /// <summary>Which schema applies, and whether the document is to be validated against it.</summary>
    private sealed record Application(string Status, bool Validate, CompiledSchema? Set);

    private Application Applied(
        ProjectRoot root,
        string filePath,
        string content,
        SchemaAssociation association,
        ProjectXmlResolver resolver,
        List<TextFinding> findings,
        List<string> dependsOn)
    {
        if (association.IsSchema)
        {
            // Requirement 5.5: a schema is checked as a schema, from its text as typed.
            var compiled = _schemas.CompileText(root, filePath, content);
            dependsOn.AddRange(compiled.Files);
            if (compiled.Problem is { } problem)
            {
                var at = string.Equals(problem.FullPath, Path.GetFullPath(filePath), StringComparison.Ordinal)
                    ? new TextPositions(content).NameAt(problem.Line, problem.Column)
                    : (0, 0);
                findings.Add(new TextFinding(at.Item1, at.Item2, TextFindingSeverity.Error, Named(problem), SchemaValidation.Source));
                return new Application("XSD: does not compile.", false, null);
            }

            return new Application(compiled.Version11 is null ? "XSD: compiles." : "XSD 1.1: not checked.", false, null);
        }

        return association.Xsd.Count > 0
            ? AppliedXsd(root, filePath, association, resolver, findings, dependsOn)
            : association.HasDoctype
                ? new Application(DtdChecked, true, null)
                : new Application(NoSchema, false, null);
    }

    private Application AppliedXsd(
        ProjectRoot root,
        string filePath,
        SchemaAssociation association,
        ProjectXmlResolver resolver,
        List<TextFinding> findings,
        List<string> dependsOn)
    {
        var resolved = ResolvedXsd(association, filePath, resolver, findings, dependsOn);
        if (resolved.Count == 0)
        {
            return new Application(NoSchema, false, null);
        }

        var compiled = _schemas.Get(root, Roots(resolved));
        dependsOn.AddRange(compiled.Files);
        if (compiled.Version11 is { } version11)
        {
            var reference = resolved.First(schema => schema.FullPath == version11).Reference;
            findings.Add(Info(reference, $"'{reference.Location}' declares XSD 1.1, which this editor does not check: it checks XSD 1.0."));
            return new Application("Schema not supported: XSD 1.1.", false, null);
        }

        if (compiled.Problem is { } problem)
        {
            // Requirement 6.6: said once, where the schema is named, and no schema findings.
            findings.Add(Info(resolved[0].Reference, Named(problem)));
            return new Application($"Schema: {Path.GetFileName(problem.FullPath ?? resolved[0].FullPath)} does not compile.", false, null);
        }

        var names = resolved
            .Select(schema => schema.Reference.Namespace.Length > 0 ? schema.Reference.Namespace : Path.GetFileName(schema.FullPath))
            .Distinct(StringComparer.Ordinal);
        return new Application($"Schema: {string.Join(", ", names)}", true, compiled);
    }

    /// <summary>The XSDs the document names that are in the project; the rest become information findings where they are named.</summary>
    private static List<(SchemaReference Reference, string FullPath)> ResolvedXsd(
        SchemaAssociation association,
        string filePath,
        ProjectXmlResolver resolver,
        List<TextFinding> findings,
        List<string> dependsOn)
    {
        var documentPath = resolver.ProjectPathOf(filePath) ?? [Path.GetFileName(filePath)];
        var resolved = new List<(SchemaReference Reference, string FullPath)>();
        foreach (var reference in association.Xsd)
        {
            switch (SchemaReferenceRules.Resolve(reference.Location, documentPath, new HashSet<string>()))
            {
                case SchemaTarget.Project project when File.Exists(resolver.FullPathOf(project.Path)):
                    resolved.Add((reference, Path.GetFullPath(resolver.FullPathOf(project.Path))));
                    break;
                case SchemaTarget.Project project:
                    dependsOn.Add(Path.GetFullPath(resolver.FullPathOf(project.Path)));
                    findings.Add(Info(reference, $"'{reference.Location}' is not in the project, so no schema is checked from it."));
                    break;
                case SchemaTarget.None none:
                    findings.Add(Info(reference, $"'{reference.Location}' {none.Reason}."));
                    break;
            }
        }

        return resolved;
    }

    private static string[] Roots(List<(SchemaReference Reference, string FullPath)> resolved) =>
        resolved.Select(schema => schema.FullPath).Distinct(StringComparer.Ordinal).ToArray();

    private static TextFinding Info(SchemaReference reference, string message) =>
        new(reference.From, reference.To, TextFindingSeverity.Info, message, SchemaValidation.Source);

    private static string Named(SchemaProblem problem)
    {
        var file = problem.FullPath is null ? "The schema" : $"'{Path.GetFileName(problem.FullPath)}'";
        var where = problem.Line > 0 ? $" (line {problem.Line})" : "";
        return $"{file} does not compile: {problem.Sentence}{where}";
    }
}
