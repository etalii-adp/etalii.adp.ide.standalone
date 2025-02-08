using Microsoft.EntityFrameworkCore;

namespace EtAlii.Adp.Api;

public static class DbContextOptionBuilderUseAdpDatabaseExtension
{
#if DEBUG
    private static readonly string ConnectionString;

    static DbContextOptionBuilderUseAdpDatabaseExtension()
    {
        var folder = Environment.SpecialFolder.LocalApplicationData;
        var path = Environment.GetFolderPath(folder);
        var databaseFile = Path.Join(path, "database.db");
        //var databaseFile = Path.Combine("Data", "database.db");
        ConnectionString = $"Data Source={databaseFile}";

    }
#endif

    public static void UseAdpDatabase(this DbContextOptionsBuilder options)
    {
#if DEBUG

        options
            .UseSqlite(ConnectionString, sqliteOptions =>
            {
                sqliteOptions.UseNetTopologySuite();     
            });
        //.UseLoggerFactory(s.GetRequiredService<ILoggerFactory>());
#else
        services.AddDbContext<DiagramsContext>(options => options.UseInMemory());
#endif
    }
}