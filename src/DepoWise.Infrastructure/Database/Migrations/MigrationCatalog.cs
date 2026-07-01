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
        new Migration007_Vehicles(),
        new Migration008_Maintenance(),
        new Migration009_FuelDailyActivity(),
        new Migration010_Requests(),
        new Migration011_Sync(),
        new Migration012_Releases(),
        new Migration013_RememberTokens(),
        new Migration014_UserBranch(),
        new Migration015_UserButtonPermissions(),
        new Migration016_CompanyFields(),
        new Migration017_StockDocFields(),
        new Migration018_ReleaseDownloadUrl(),
        new Migration019_PermissionTemplates(),
        new Migration020_TemplateRole(),
    };
}
