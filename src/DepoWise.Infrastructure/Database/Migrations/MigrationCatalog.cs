namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>Yerel SQLite migration kataloğu (tek doğru kaynak, artan sürüm).</summary>
public static class MigrationCatalog
{
    public static IReadOnlyList<IMigration> All() => new IMigration[]
    {
        new Migration001_CoreSchema(),
        new Migration002_AuthSeed(),
    };
}
