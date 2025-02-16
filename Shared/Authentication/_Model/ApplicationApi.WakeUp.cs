namespace EtAlii.Adp;

public static partial class ApplicationApi
{
    public static class WakeUp
    {
        public static class Head
        {
            public const string Request = $"/api/{Function}";
            public const string Function = "wakeup";
        }
    }
}