using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Sparql.Tests;

/// <summary>
/// Each example's drawn elements as they were recorded: every id, position, type and payload byte, in order.
/// </summary>
/// <remarks>
/// The session and selection tests hold properties - the header travels, a culled element is not resent -
/// which several drawings satisfy at once. This one holds the drawing itself, so that a change meant to leave
/// it alone (spec 025, T315) is seen to. The inputs reach every kind the mapper draws: regions, the four node
/// kinds, edges, annotations, the header band and, through a generated query past the sanity bound, the banner.
/// A change meant to alter the drawing records the new digests here.
/// </remarks>
public sealed class SparqlElementMapperRecordedTests
{
    public static TheoryData<string, string> Recorded => new()
    {
        { "examples/adp/ask.rq", "84B411BD62EAA13B" },
        { "examples/uniprot/describe-embl-cds.rq", "7D906FF37E9D7988" },
        { "examples/uniprot/proteome-location-of-gene.rq", "1CBE6135A01F3BE3" },
        { "examples/w3c-sparql/aggregate.rq", "BF8AACDBCF429F1F" },
        { "examples/w3c-sparql/construct.rq", "8A9465CD700FD95F" },
        { "examples/w3c-sparql/filter.rq", "75BB40EFC027EB6D" },
        { "examples/w3c-sparql/optional.rq", "BDAF6437F0F7D9B9" },
        { "examples/w3c-sparql/property-path.rq", "C8FB5F02307FE27D" },
        { "examples/w3c-sparql/simple.rq", "BBAE0AF2327B4F2A" },
        { "examples/w3c-sparql/union.rq", "5FAE857D17167250" },
        { "Fixtures/constructs.rq", "11B2DC7638EAA702" },
        { "Fixtures/groups.rq", "5A930A7192FC6B4B" },
        { "Fixtures/describe-form.rq", "22514D7903470703" },
        { "Fixtures/construct-form.rq", "1A536432F0FEBD48" },
        { Truncated, "D0603C07373DA887" },
    };

    /// <summary>The name of the generated query that passes the sanity bound, so the banner is drawn.</summary>
    private const string Truncated = "generated: past the sanity bound";

    [Theory]
    [MemberData(nameof(Recorded))]
    public void AQuery_IsDrawnAsRecorded(string name, string digest)
    {
        // Arrange.
        var projection = SparqlProjection.Project(SparqlParser.Parse(Text(name)));
        var layout = SparqlLayout.Compute(projection, new Dictionary<string, RegistrationPosition>(StringComparer.Ordinal));

        // Act.
        var described = Describe(new SparqlElementMapper().Visible(projection, layout, DiagramViewport.Unbounded));

        // Assert.
        Assert.True(digest == Digest(described), $"{name} is drawn as {Digest(described)}:\n{described}");
    }

    private static string Text(string name)
    {
        if (name == Truncated)
        {
            var text = new StringBuilder("PREFIX ex: <http://example.org/>\nSELECT * WHERE {\n");
            for (var i = 0; i < 200; i++)
            {
                text.Append(CultureInfo.InvariantCulture, $"  ?s{i} ex:p{i} ?o{i} .\n");
            }

            return text.Append("}\n").ToString();
        }

        return name.StartsWith("Fixtures/", StringComparison.Ordinal)
            ? File.ReadAllText(IoPath.Combine(AppContext.BaseDirectory, "Fixtures", name["Fixtures/".Length..]))
            : File.ReadAllText(RepositoryPaths.Under(["src", "diagrams", "sparql", .. name.Split('/')]));
    }

    private static string Describe(IReadOnlyList<DiagramElement> elements)
    {
        var text = new StringBuilder();
        foreach (var element in elements)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"{element.Id} {element.X:R} {element.Y:R} {element.Type} {element.PayloadTypeUrl} {Convert.ToHexString(element.Payload.Span)}");
        }

        return text.ToString();
    }

    private static string Digest(string described) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(described)))[..16];
}
