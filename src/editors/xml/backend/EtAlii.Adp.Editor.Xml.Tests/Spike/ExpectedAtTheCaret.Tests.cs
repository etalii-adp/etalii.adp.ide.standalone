using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.Schema;
using Xunit;

namespace EtAlii.Adp.Editor.Xml.Tests.Spike;

/// <summary>
/// The spike of xml-editor Requirement 1, kept as tests: what the platform's
/// <see cref="XmlSchemaValidator"/> answers when it is fed a document that stops at the caret.
/// Completion is built on these answers, so they are held here rather than remembered. Each
/// test feeds the text before the caret and asserts what <c>GetExpectedParticles</c> or
/// <c>GetExpectedAttributes</c> said.
/// </summary>
public class ExpectedAtTheCaretTests
{
    private const string Namespace = "urn:adp:cities";

    /// <summary>
    /// A sequence, a choice inside it, an all-group, a required enumerated attribute and an
    /// optional one, in a target namespace - the schema of the design's scratch run.
    /// </summary>
    private const string CitiesSchema = """
        <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:adp:cities" xmlns="urn:adp:cities" elementFormDefault="qualified">
          <xs:element name="cities">
            <xs:complexType>
              <xs:sequence>
                <xs:element name="title" type="xs:string"/>
                <xs:element name="city" maxOccurs="unbounded">
                  <xs:complexType>
                    <xs:sequence>
                      <xs:element name="name" type="xs:string"/>
                      <xs:choice>
                        <xs:element name="mayor" type="xs:string"/>
                        <xs:element name="council" type="xs:string"/>
                      </xs:choice>
                      <xs:element name="facts">
                        <xs:complexType>
                          <xs:all>
                            <xs:element name="area" type="xs:decimal"/>
                            <xs:element name="founded" type="xs:gYear"/>
                          </xs:all>
                        </xs:complexType>
                      </xs:element>
                    </xs:sequence>
                    <xs:attribute name="kind" use="required">
                      <xs:simpleType>
                        <xs:restriction base="xs:string">
                          <xs:enumeration value="capital"/>
                          <xs:enumeration value="town"/>
                        </xs:restriction>
                      </xs:simpleType>
                    </xs:attribute>
                    <xs:attribute name="population" type="xs:int"/>
                  </xs:complexType>
                </xs:element>
              </xs:sequence>
            </xs:complexType>
          </xs:element>
        </xs:schema>
        """;

    private const string Root = """<cities xmlns="urn:adp:cities">""";
    private const string City = """<city kind="town"><name>Delft</name>""";

    public static TheoryData<string, string, string[]> TheNineCarets() => new()
    {
        { "nothing typed", "", ["cities"] },
        { "inside the root, empty", Root, ["title"] },
        { "in the sequence, after title", Root + "<title>T</title>", ["city"] },
        { "inside city, empty", Root + "<title>T</title><city kind=\"town\">", ["name"] },
        { "at the choice, after name", Root + "<title>T</title>" + City, ["council", "mayor"] },
        { "after the choice", Root + "<title>T</title>" + City + "<mayor>M</mayor>", ["facts"] },
        { "inside the all-group, empty", Root + "<title>T</title>" + City + "<mayor>M</mayor><facts>", ["area", "founded"] },
        { "inside the all-group, after founded", Root + "<title>T</title>" + City + "<mayor>M</mayor><facts><founded>1246</founded>", ["area"] },
        { "after a closed city", Root + "<title>T</title>" + City + "<mayor>M</mayor><facts><area>24</area><founded>1246</founded></facts></city>", ["city"] },
    };

    [Theory]
    [MemberData(nameof(TheNineCarets))]
    public void TheValidator_AnswersTheElementsExpected_ForADocumentThatStopsAtTheCaret(string caret, string before, string[] expected)
    {
        // Arrange and act.
        var answered = Feed(CitiesSchema, before, out _).ExpectedElements();

        // Assert: the design's table, run again as a test.
        Assert.True(expected.SequenceEqual(answered), $"{caret}: expected [{string.Join(", ", expected)}], answered [{string.Join(", ", answered)}]");
    }

    [Fact]
    public void TheValidator_AnswersTheAttributesExpected_WhenNoneIsPresent()
    {
        // Arrange: the start tag of city is fed with no attribute.
        var fed = Feed(CitiesSchema, Root + "<title>T</title>", out _);
        var validator = fed.Validator;
        validator.ValidateElement("city", Namespace, null);

        // Act.
        var attributes = validator.GetExpectedAttributes();

        // Assert: both, and which is required is readable from the answer.
        Assert.Equal(["kind", "population"], attributes.Select(attribute => attribute.Name).Order());
        Assert.Equal(XmlSchemaUse.Required, attributes.Single(attribute => attribute.Name == "kind").Use);
    }

    [Fact]
    public void TheValidator_LeavesOutAnAttributeAlreadyPresent()
    {
        // Arrange.
        var fed = Feed(CitiesSchema, Root + "<title>T</title>", out _);
        var validator = fed.Validator;
        validator.ValidateElement("city", Namespace, null);
        validator.ValidateAttribute("kind", "", "town", null);

        // Act.
        var attributes = validator.GetExpectedAttributes();

        // Assert.
        Assert.Equal(["population"], attributes.Select(attribute => attribute.Name));
    }

    [Fact]
    public void AWildcard_IsAnsweredAsAnAnyParticle()
    {
        // Arrange: xs:any after a named element.
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="box">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="label" type="xs:string"/>
                    <xs:any namespace="##other" processContents="lax" minOccurs="0"/>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        // Act.
        var particles = Feed(schema, "<box><label>L</label>", out _).Validator.GetExpectedParticles();

        // Assert: the wildcard comes back as XmlSchemaAny, which completion leaves out.
        Assert.Contains(particles, particle => particle is XmlSchemaAny);
        Assert.DoesNotContain(particles, particle => particle is XmlSchemaElement);
    }

    [Fact]
    public void ASubstitutionGroup_IsAnsweredWithItsHeadAndEveryMember()
    {
        // Arrange: shape is the head, circle and square substitute for it.
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:element name="shape" type="xs:string"/>
              <xs:element name="circle" type="xs:string" substitutionGroup="shape"/>
              <xs:element name="square" type="xs:string" substitutionGroup="shape"/>
              <xs:element name="drawing">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element ref="shape" maxOccurs="unbounded"/>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;

        // Act.
        var answered = Feed(schema, "<drawing>", out _).ExpectedElements();

        // Assert: the platform expands the group itself, so completion offers every member
        // with no lookup of its own.
        Assert.Equal(["circle", "shape", "square"], answered);
    }

    [Fact]
    public void ATypeDerivedByExtension_ExpectsTheBaseContentFirst_ThenItsOwn()
    {
        // Arrange: address extends place, adding street after place's name.
        const string schema = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema">
              <xs:complexType name="place">
                <xs:sequence>
                  <xs:element name="name" type="xs:string"/>
                </xs:sequence>
              </xs:complexType>
              <xs:complexType name="address">
                <xs:complexContent>
                  <xs:extension base="place">
                    <xs:sequence>
                      <xs:element name="street" type="xs:string"/>
                    </xs:sequence>
                  </xs:extension>
                </xs:complexContent>
              </xs:complexType>
              <xs:element name="home" type="address"/>
            </xs:schema>
            """;

        // Act.
        var first = Feed(schema, "<home>", out _).ExpectedElements();
        var second = Feed(schema, "<home><name>N</name>", out _).ExpectedElements();

        // Assert.
        Assert.Equal(["name"], first);
        Assert.Equal(["street"], second);
    }

    [Fact]
    public void TwoNamespaces_AreAnsweredEachWithItsOwnNamespace()
    {
        // Arrange: an order in one namespace that holds a party of another.
        const string orders = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:adp:orders" xmlns:p="urn:adp:parties" elementFormDefault="qualified">
              <xs:import namespace="urn:adp:parties"/>
              <xs:element name="order">
                <xs:complexType>
                  <xs:sequence>
                    <xs:element name="number" type="xs:string"/>
                    <xs:element ref="p:party"/>
                  </xs:sequence>
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;
        const string parties = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema" targetNamespace="urn:adp:parties" elementFormDefault="qualified">
              <xs:element name="party" type="xs:string"/>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet { XmlResolver = null };
        schemas.Add(XmlSchema.Read(new StringReader(parties), null)!);
        schemas.Add(XmlSchema.Read(new StringReader(orders), null)!);
        schemas.Compile();

        // Act.
        var particles = Feed(schemas, """<order xmlns="urn:adp:orders" xmlns:p="urn:adp:parties"><number>1</number>""").Validator.GetExpectedParticles();

        // Assert: the qualified name carries the other namespace, so the prefix in scope can be found.
        var element = Assert.IsType<XmlSchemaElement>(Assert.Single(particles));
        Assert.Equal(new XmlQualifiedName("party", "urn:adp:parties"), element.QualifiedName);
    }

    [Fact]
    public void TheTimings_OfValidationCompletionAndCompilation_AreMeasured()
    {
        // Arrange: an XSD of at least 2,000 lines and a document of at least 5,000 that
        // satisfies it, the sizes of Requirement 1.2.
        var (schemaText, documentText) = LargeSchemaAndDocument();
        Assert.True(schemaText.Split('\n').Length >= 2_000);
        Assert.True(documentText.Split('\n').Length >= 5_000);
        const int runs = 20;

        // Act: compile, validate the whole document, and one completion in the middle.
        var compile = Measure(runs, () => Compile(schemaText));
        var schemas = Compile(schemaText);
        var validate = Measure(runs, () => Validate(schemas, documentText));
        var middle = documentText.IndexOf("\n  <item", documentText.Length / 2, StringComparison.Ordinal) + 3;
        var completion = Measure(runs, () => Feed(schemas, documentText[..middle]).Validator.GetExpectedParticles());

        // Assert: the figures go to the output and into the design; the bounds of the
        // Performance section (one second for findings, 300 ms for completion) are the bar.
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"schema {schemaText.Split('\n').Length} lines, document {documentText.Split('\n').Length} lines; " +
            $"compile {compile:F1} ms, validate {validate:F1} ms, one completion {completion:F1} ms (mean of {runs})");
        Assert.True(validate < 1_000, $"validation took {validate:F1} ms");
        Assert.True(completion < 300, $"completion took {completion:F1} ms");
    }

    private static double Measure(int runs, Action action)
    {
        action();
        var watch = Stopwatch.StartNew();
        for (var run = 0; run < runs; run++)
        {
            action();
        }

        return watch.Elapsed.TotalMilliseconds / runs;
    }

    private static XmlSchemaSet Compile(string schemaText)
    {
        var schemas = new XmlSchemaSet { XmlResolver = null };
        schemas.Add(XmlSchema.Read(new StringReader(schemaText), null)!);
        schemas.Compile();
        return schemas;
    }

    private static void Validate(XmlSchemaSet schemas, string documentText)
    {
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas, XmlResolver = null };
        var errors = 0;
        settings.ValidationEventHandler += (_, _) => errors++;
        using var reader = XmlReader.Create(new StringReader(documentText), settings);
        while (reader.Read())
        {
        }

        Assert.Equal(0, errors);
    }

    /// <summary>
    /// 250 complex types of the same shape, each eight lines, and a catalog whose items take
    /// any of them; the document is items of every type in turn.
    /// </summary>
    private static (string Schema, string Document) LargeSchemaAndDocument()
    {
        const int types = 250;
        var schema = new StringBuilder();
        schema.Append("<xs:schema xmlns:xs=\"http://www.w3.org/2001/XMLSchema\">\n");
        schema.Append("  <xs:element name=\"catalog\">\n    <xs:complexType>\n      <xs:choice maxOccurs=\"unbounded\">\n");
        for (var type = 0; type < types; type++)
        {
            schema.Append($"        <xs:element name=\"item{type}\" type=\"t{type}\"/>\n");
        }

        schema.Append("      </xs:choice>\n    </xs:complexType>\n  </xs:element>\n");
        for (var type = 0; type < types; type++)
        {
            schema.Append($"  <xs:complexType name=\"t{type}\">\n    <xs:sequence>\n");
            schema.Append("      <xs:element name=\"title\" type=\"xs:string\"/>\n");
            schema.Append("      <xs:element name=\"amount\" type=\"xs:decimal\" minOccurs=\"0\"/>\n");
            schema.Append("    </xs:sequence>\n");
            schema.Append("    <xs:attribute name=\"code\" type=\"xs:string\" use=\"required\"/>\n");
            schema.Append("  </xs:complexType>\n");
        }

        schema.Append("</xs:schema>\n");

        var document = new StringBuilder();
        document.Append("<catalog>\n");
        for (var item = 0; item < 1_250; item++)
        {
            var type = item % types;
            document.Append($"  <item{type} code=\"c{item}\">\n    <title>Item {item}</title>\n    <amount>{item}.5</amount>\n  </item{type}>\n");
        }

        document.Append("</catalog>\n");

        return (schema.ToString(), document.ToString());
    }

    /// <summary>What was fed, and the validator left at the caret.</summary>
    private sealed record Fed(XmlSchemaValidator Validator, XmlSchemaSet Schemas)
    {
        public string[] ExpectedElements() => Validator.GetExpectedParticles()
            .OfType<XmlSchemaElement>()
            .Select(element => element.QualifiedName.Name)
            .Order()
            .ToArray();
    }

    private static Fed Feed(string schemaText, string before, out XmlSchemaSet schemas)
    {
        schemas = Compile(schemaText);
        return Feed(schemas, before);
    }

    /// <summary>
    /// Feeds the validator the text before the caret, event by event, until the reader runs
    /// out of text - which, for a document that stops at the caret, it does by throwing.
    /// </summary>
    private static Fed Feed(XmlSchemaSet schemas, string before)
    {
        var names = new NameTable();
        var scopes = new XmlNamespaceManager(names);
        var validator = new XmlSchemaValidator(names, schemas, scopes, XmlSchemaValidationFlags.None);
        validator.ValidationEventHandler += (_, _) => { };
        validator.Initialize();

        var settings = new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Ignore };
        using var reader = XmlReader.Create(new StringReader(before), settings);
        try
        {
            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                        var empty = reader.IsEmptyElement;
                        scopes.PushScope();
                        var attributes = new List<(string Local, string Namespace, string Value)>();
                        if (reader.MoveToFirstAttribute())
                        {
                            do
                            {
                                if (reader.NamespaceURI == "http://www.w3.org/2000/xmlns/")
                                {
                                    scopes.AddNamespace(reader.Prefix == "xmlns" ? reader.LocalName : "", reader.Value);
                                }
                                else
                                {
                                    attributes.Add((reader.LocalName, reader.NamespaceURI, reader.Value));
                                }
                            }
                            while (reader.MoveToNextAttribute());

                            reader.MoveToElement();
                        }

                        validator.ValidateElement(reader.LocalName, reader.NamespaceURI, null);
                        foreach (var (local, ns, value) in attributes)
                        {
                            validator.ValidateAttribute(local, ns, value, null);
                        }

                        validator.ValidateEndOfAttributes(null);
                        if (empty)
                        {
                            validator.ValidateEndElement(null);
                            scopes.PopScope();
                        }

                        break;
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                        validator.ValidateText(reader.Value);
                        break;
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        validator.ValidateWhitespace(reader.Value);
                        break;
                    case XmlNodeType.EndElement:
                        validator.ValidateEndElement(null);
                        scopes.PopScope();
                        break;
                }
            }
        }
        catch (XmlException)
        {
            // The text stops at the caret: the reader says so by throwing, and the validator
            // stands where the last event left it.
        }

        return new Fed(validator, schemas);
    }
}
