using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public static class DbContextOptionBuilderUseAdpDatabaseExtension
{
    private static readonly string ConnectionString;
    
    static DbContextOptionBuilderUseAdpDatabaseExtension()
    {
// #if DEBUG
//         var folder = Environment.SpecialFolder.LocalApplicationData;
//         var path = Environment.GetFolderPath(folder);
//         var databaseFile = Path.Join(path, "database.db");
//         ConnectionString = $"Data Source={databaseFile}";
// #else
        ConnectionString = "Server=tcp:etalii-adp.database.windows.net,1433;Initial Catalog=etalii-adp;Persist Security Info=False;User ID=adp-admin;Password=11gghh22_EtAlii;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;";
// #endif
    }


    public static void UseAdpDatabase(this DbContextOptionsBuilder options)
    {
// #if DEBUG
//         options
//             .UseSqlite(ConnectionString, sqliteOptions =>
//             {
//             });
//         //.UseLoggerFactory(s.GetRequiredService<ILoggerFactory>());
// #else
        options
            .UseSqlServer(ConnectionString, sqlOptions =>
            {
                sqlOptions.EnableRetryOnFailure();
//                sqlOptions.UseVectorSearch();
            });
// #endif
    }
}