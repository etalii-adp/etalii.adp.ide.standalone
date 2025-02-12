namespace EtAlii.Adp;

public static partial class ApplicationApi
{
    public static class Authentication
    {
        public static class Get
        {
            public const string Request = $"/api/{Function}";
            public const string Function = "authentication-login";
        }
    }
}