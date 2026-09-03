namespace EtAlii.Adp.Diagram.Rdf;

/// <summary>The handful of well-known IRIs the parser and the readings speak of by name.</summary>
public static class RdfVocabulary
{
    /// <summary>What the keyword <c>a</c> means in verb position.</summary>
    public const string Type = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>The head pointer of a collection's cons pair.</summary>
    public const string First = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";

    /// <summary>The tail pointer of a collection's cons pair.</summary>
    public const string Rest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";

    /// <summary>The empty collection, and every collection's final tail.</summary>
    public const string Nil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>The datatype of a language-tagged literal.</summary>
    public const string LangString = "http://www.w3.org/1999/02/22-rdf-syntax-ns#langString";

    /// <summary>The implied datatype of a plain literal.</summary>
    public const string XsdString = "http://www.w3.org/2001/XMLSchema#string";

    /// <summary>The datatype of a bare whole number.</summary>
    public const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The datatype of a bare number with a decimal point.</summary>
    public const string XsdDecimal = "http://www.w3.org/2001/XMLSchema#decimal";

    /// <summary>The datatype of a bare number with an exponent.</summary>
    public const string XsdDouble = "http://www.w3.org/2001/XMLSchema#double";

    /// <summary>The datatype of the bare keywords <c>true</c> and <c>false</c>.</summary>
    public const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";
}
