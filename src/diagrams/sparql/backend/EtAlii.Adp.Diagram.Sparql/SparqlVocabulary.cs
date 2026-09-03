namespace EtAlii.Adp.Diagram.Sparql;

/// <summary>The handful of well-known IRIs the parser itself needs: what <c>a</c> means, what a collection expands to, and the datatypes bare literals carry.</summary>
public static class SparqlVocabulary
{
    /// <summary>What the keyword <c>a</c> means in verb position.</summary>
    public const string Type = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type";

    /// <summary>A collection cell's head.</summary>
    public const string First = "http://www.w3.org/1999/02/22-rdf-syntax-ns#first";

    /// <summary>A collection cell's tail.</summary>
    public const string Rest = "http://www.w3.org/1999/02/22-rdf-syntax-ns#rest";

    /// <summary>The empty collection.</summary>
    public const string Nil = "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil";

    /// <summary>The datatype of a bare integer literal.</summary>
    public const string XsdInteger = "http://www.w3.org/2001/XMLSchema#integer";

    /// <summary>The datatype of a bare decimal literal.</summary>
    public const string XsdDecimal = "http://www.w3.org/2001/XMLSchema#decimal";

    /// <summary>The datatype of a bare double literal.</summary>
    public const string XsdDouble = "http://www.w3.org/2001/XMLSchema#double";

    /// <summary>The datatype of <c>true</c> and <c>false</c>.</summary>
    public const string XsdBoolean = "http://www.w3.org/2001/XMLSchema#boolean";
}
