namespace EtAlii.Adp;
 
public static partial class ApplicationApi
{
    public static partial class Diagrams
    {
        public static class Changes
        {
            public static string Request(DiagramIdentifier id) => $"/api/{Function}/{id.Identifier}";
            public const string Function = "diagrams-changes";
            public const string Route = $"{Function}/{{id:guid}}";
        }
    }
}