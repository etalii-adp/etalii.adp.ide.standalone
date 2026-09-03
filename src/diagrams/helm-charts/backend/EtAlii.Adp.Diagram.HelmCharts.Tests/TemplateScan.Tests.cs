using Xunit;

namespace EtAlii.Adp.Diagram.HelmCharts.Tests;

/// <summary>
/// The literal/templated boundary, pinned case by case: these tests are the specification of
/// what counts as literally written (Requirement 3.2, the design's one-place NFR).
/// </summary>
public class TemplateScanTests
{
    [Fact]
    public void ALiteralKind_IsDiscovered()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("apiVersion: apps/v1\nkind: Deployment\nmetadata:\n  name: x\n");

        // Assert.
        Assert.Equal(["Deployment"], facts.Kinds);
        Assert.Equal(["apps/v1"], facts.ApiVersions);
    }

    [Fact]
    public void ATemplatedKind_IsUnknownByDesign()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("kind: {{ .Values.kind }}\napiVersion: {{ include \"x.apiVersion\" . }}\n");

        // Assert.
        // Never guessed - but the include's literal NAME is still a reference fact.
        Assert.Empty(facts.Kinds);
        Assert.Empty(facts.ApiVersions);
        Assert.Equal(["x.apiVersion"], facts.References);
    }

    [Fact]
    public void AQuotedKind_LosesItsQuotes()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("kind: \"Service\"\n");

        // Assert.
        Assert.Equal(["Service"], facts.Kinds);
    }

    [Fact]
    public void ATrailingComment_IsNotPartOfTheValue()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("kind: Ingress # the optional one\n");

        // Assert.
        Assert.Equal(["Ingress"], facts.Kinds);
    }

    [Fact]
    public void AnIndentedKind_BelongsToSomethingElse()
    {
        // Arrange.
        // An RBAC rule, a list entry, a commented example: only a column-zero kind is the
        // document's own.
        const string text = "kind: ClusterRole\nrules:\n  - kind: Pod\n# kind: Commented\n";

        // Act.
        var facts = TemplateScan.Scan(text);

        // Assert.
        Assert.Equal(["ClusterRole"], facts.Kinds);
    }

    [Fact]
    public void AMultiDocumentTemplate_ListsEveryKindOnceSorted()
    {
        // Arrange.
        const string text = "kind: Service\n---\nkind: Deployment\n---\nkind: Service\n";

        // Act.
        var facts = TemplateScan.Scan(text);

        // Assert.
        Assert.Equal(["Deployment", "Service"], facts.Kinds);
    }

    [Fact]
    public void ADefineBlock_ExportsItsLiteralName()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("{{- define \"mychart.labels\" -}}\napp: x\n{{- end }}\n");

        // Assert.
        Assert.Equal(["mychart.labels"], facts.Defines);
    }

    [Fact]
    public void IncludeAndTemplate_BothReference()
    {
        // Arrange.
        const string text = "  labels: {{ include \"mychart.labels\" . | nindent 4 }}\n  {{ template \"mychart.name\" . }}\n";

        // Act.
        var facts = TemplateScan.Scan(text);

        // Assert.
        Assert.Equal(["mychart.labels", "mychart.name"], facts.References);
    }

    [Fact]
    public void AComputedIncludeName_IsNoReference()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("{{ include (printf \"%s.labels\" .Chart.Name) . }}\n");

        // Assert.
        // The name is not a literal double-quoted string, so nothing is claimed about it.
        Assert.Empty(facts.References);
    }

    [Fact]
    public void ProseLikeNotesTxt_YieldsNothing()
    {
        // Arrange.
        const string text = "Thank you for installing {{ .Chart.Name }}.\n\nYour release is named {{ .Release.Name }}.\n";

        // Act.
        var facts = TemplateScan.Scan(text);

        // Assert.
        Assert.Empty(facts.Kinds);
        Assert.Empty(facts.ApiVersions);
        Assert.Empty(facts.Defines);
        Assert.Empty(facts.References);
    }

    [Fact]
    public void SeveralFactsOnOneLine_AllCount()
    {
        // Arrange & Act.
        var facts = TemplateScan.Scan("{{ include \"a.x\" . }}{{ include \"b.y\" . }}\n");

        // Assert.
        Assert.Equal(["a.x", "b.y"], facts.References);
    }

    [Fact]
    public void CrlfContent_ScansTheSameAsLf()
    {
        // Arrange.
        // Fixture bytes are checkout-dependent territory everywhere else; the scanner itself
        // must not care which line ending a template arrived with.
        var lf = TemplateScan.Scan("kind: Deployment\n{{- define \"x.y\" }}\n");

        // Act.
        var crlf = TemplateScan.Scan("kind: Deployment\r\n{{- define \"x.y\" }}\r\n");

        // Assert.
        // List by list, never record by record: TemplateFacts holds IReadOnlyLists, and record
        // equality over those compares references - the exact lesson the design writes down.
        Assert.Equal(lf.Kinds, crlf.Kinds);
        Assert.Equal(lf.ApiVersions, crlf.ApiVersions);
        Assert.Equal(lf.Defines, crlf.Defines);
        Assert.Equal(lf.References, crlf.References);
        Assert.Equal(["Deployment"], crlf.Kinds);
        Assert.Equal(["x.y"], crlf.Defines);
    }
}
