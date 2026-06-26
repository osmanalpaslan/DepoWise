namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>Yerel SQLite migration kataloğu (tek doğru kaynak, artan sürüm).</summary>
public static class MigrationCatalog
{
    public static IReadOnlyList<IMigration> All() => new IMigration[]
    {
        new Migration001_CoreSchema(),
        new Migration002_AuthSeed(),
        new Migration003_AppSettings(),
        new Migration004_Personnel(),
        new Migration005_Materials(),
        new Migration006_StockDocuments(),
    };
}
