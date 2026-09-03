using Xunit;
using IoPath = System.IO.Path;

namespace EtAlii.Adp.Diagram.Rdf.Tests;

/// <summary>
/// The language: header's band guard (skos-diagram Requirement 3.2): honoured in the safe band,
/// ignored-and-reported above a core header it would sever, and never guessed at. The hazard is
/// precise - core's pairing scan stops at the first unknown line, so a body:/view: BELOW the
/// header is never reached - and so is the check.
/// </summary>
public class SkosRegistrationLanguageTests : IDisposable
{
    private readonly string _root;

    public SkosRegistrationLanguageTests()
    {
        _root = IoPath.Combine(IoPath.GetTempPath(), "EtAlii.Adp.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        TestFolder.TryDelete(_root);
    }

    private string Write(string content)
    {
        var path = IoPath.Combine(_root, Guid.NewGuid().ToString("N") + ".adp");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void AHeaderInTheSafeBand_IsHonoured_AndLowercased()
    {
        // Arrange: after body:/view:, before layout: - the facility's band.
        var path = Write("w3c/skos\nbody: scheme.ttl\nview: whatever\nlanguage: NL\nlayout:\n  res:x 1 2\n");

        // Act & assert.
        Assert.Equal(("nl", 0), SkosRegistrationLanguage.Read(path));
    }

    [Fact]
    public void AHeaderAboveABodyLine_IsIgnored_AndItsLineReported()
    {
        // Arrange: the severing position - core's scan would stop at language: and never read body:.
        var path = Write("w3c/skos\nlanguage: nl\nbody: scheme.ttl\n");

        // Act.
        var (language, misplacedLine) = SkosRegistrationLanguage.Read(path);

        // Assert: ignored (the default order applies), and named for the validator - never guessed at.
        Assert.Null(language);
        Assert.Equal(2, misplacedLine);
    }

    [Fact]
    public void AHeaderWithNoCoreHeaderBelow_IsInTheBandByConstruction()
    {
        // Arrange: a derived-body registration - no body: line at all. There is nothing to
        // sever, so the header stands wherever the band began.
        var path = Write("w3c/skos\nlanguage: de\n");

        // Act & assert.
        Assert.Equal(("de", 0), SkosRegistrationLanguage.Read(path));
    }

    [Fact]
    public void OtherReadingsHeaders_AreLeftAlone_AndLayoutEndsTheScan()
    {
        // Arrange: an unknown key is not ours to interpret; a language: after layout: is not a header.
        var path = Write("w3c/skos\nbody: scheme.ttl\nresource: something\nlayout:\n  language: fr\n");

        // Act & assert.
        Assert.Equal((null, 0), SkosRegistrationLanguage.Read(path));
    }

    [Fact]
    public void NoRegistration_MeansTheDefaultOrder()
    {
        // Act & assert.
        Assert.Equal((null, 0), SkosRegistrationLanguage.Read(null));
        Assert.Equal((null, 0), SkosRegistrationLanguage.Read(IoPath.Combine(_root, "absent.adp")));
    }

    [Fact]
    public void TheRead_SucceedsWhileAWriterHoldsTheRegistration()
    {
        // Arrange: the shared-read discipline - a rename's in-place .adp rewrite must not be
        // refused by, or refuse, this scan.
        var path = Write("w3c/skos\nbody: scheme.ttl\nlanguage: nl\n");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.Read);

        // Act & assert.
        Assert.Equal(("nl", 0), SkosRegistrationLanguage.Read(path));
    }
}
