namespace EtAlii.Adp;

public static partial class ApplicationPath
{
    public static partial class Diagrams
    {
        public static class Trends
        {
            public const string GetTrendDiagramRequest = $"/api/diagrams/{GetTrendDiagramFunction}";
            public const string GetTrendDiagramFunction = "trend/get";
        }
    }
}