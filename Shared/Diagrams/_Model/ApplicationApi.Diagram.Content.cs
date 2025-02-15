namespace EtAlii.Adp;

public static partial class ApplicationApi
{
    public static class Diagram
    {
        public static class Content
        {
            public static string Request(DiagramIdentifier id) => $"/api/{Function}/{id}";
            public const string Function = "diagram-content";
            public const string Route = $"{Function}/{{id:guid}}";
        }
    }
}