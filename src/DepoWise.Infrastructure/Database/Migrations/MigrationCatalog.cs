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
        new Migration021_MachineQuota(),
        new Migration022_MachineIpUserQuota(),
        new Migration023_MachineIpV4V6(),
        new Migration024_BranchCodePassword(),
        new Migration025_MachineBranch(),
        new Migration026_UserViewAllBranches(),
        new Migration027_OperatingBranch(),
        new Migration028_DataConflicts(),
        new Migration029_TwoRoleModel(),
        new Migration030_SplitPermScreens(),
        new Migration031_AlertReads(),
        new Migration032_CompanyGrantLimits(),
        new Migration033_UserPersonnelLink(),
        new Migration034_FieldStaffAndTitles(),
        new Migration035_SplitRequestApproval(),
        new Migration036_RestrictedSuperAdmin(),
        new Migration037_GrantLevel(),
        new Migration038_CompanyMaxAdmins(),
        new Migration039_TemplateScope(),
        new Migration040_MaterialTemplates(),
        new Migration041_RoleGrantLimits(),
        new Migration042_MustChangePassword(),
        new Migration043_TemplateCompatibleVehicles(),
        new Migration044_SpecialCodeAndPurge(),
        new Migration045_CompanyLocalReset(),
        new Migration046_MachineReset(),
        new Migration047_UserListPreferences(),
        new Migration048_NormalizeMaterialType(),
        new Migration049_ListPreferenceExtras(),
        new Migration050_NormalizeLookupSpaces(),
        new Migration051_LookupLocked(),
        new Migration052_FuelRecipient(),
        new Migration053_PostgresTurkishCollations(),
        new Migration054_MaterialTemplateLink(),
        new Migration055_MaterialBranch(),
        new Migration056_ExportPermission(),
        new Migration057_StockChangeLog(),
        new Migration058_ListPreferencePinnedSort(),
        new Migration059_MaintenanceTeamStock(),
        new Migration060_RequestOperations(),
        new Migration061_RequestOperationFields(),
        new Migration062_ChildTableCompanyId(),
        new Migration063_UserUsernameActiveUnique(),
        new Migration064_StockBalanceLocation(),
        new Migration065_ScreenPlatformVisibility(),
        new Migration066_Parties(),
        new Migration067_Invoices(),
        new Migration068_Finance(),
        new Migration069_ReversalSyncStamp(),
        new Migration070_MenuLayout(),
        new Migration071_MenuSection(),
        new Migration072_RoleGrantLimitsCompany(),
        new Migration073_Projects(),
        new Migration074_DocumentFields(),
        new Migration075_Equipment(),
        new Migration076_Assignments(),
        new Migration077_CostCenters(),
        new Migration078_PurchaseOrders(),
        new Migration079_WorkOrders(),
        new Migration080_CalendarEvents(),
        new Migration081_Announcements(),
        new Migration082_OperationIdCompanyScope(),
        new Migration083_CustomReports(),
        new Migration084_Teams(),
        new Migration085_ApprovalChain(),
        new Migration086_EquipmentMaintenance(),
        new Migration087_FieldRequirements(),   // 2026-09-03 (kullanıcı onayı): alan zorunluluğu — yalnız ekleme
        new Migration088_EquipmentTypeLocked(),  // 2026-09-04 HATA DÜZELTMESİ: equipment_types.is_locked eksikti
        new Migration089_DocumentFields(),       // 2026-09-04 MUH-01b: yakıt dağıtımı + iki bakım tablosuna belge no — yalnız ekleme
        new Migration090_MaintenancePartyLink(),  // 2026-09-04 MUH-01c: bakımlara cari (dış servis sağlayıcısı) — yalnız ekleme
        new Migration091_ListIndexes(),           // 2026-09-04 FAZ I: liste sorgularının indeksleri — yalnız CREATE INDEX
        new Migration092_RolePermissions(),       // 2026-09-05 FAZ 3a (ADR-222): rol bazlı ALLOW — yalnız CREATE TABLE, boş doğar
        new Migration093_FieldProtections(),      // 2026-09-05 FAZ 3b (ADR-223): korumalı alanlar — yalnız CREATE TABLE, boş doğar
        new Migration094_ConflictSnapshots(),     // 2026-09-06 FAZ 4.4: çakışmada kazanan/kaybeden görüntüsü — yalnız ADD COLUMN
        new Migration095_UserContactFields(),     // 2026-09-06 kullanıcı isteği: e-posta · telefon · unvan · not — yalnız ADD COLUMN
        new Migration096_Chat(),                  // 2026-09-06 kullanıcı isteği: uygulama içi sohbet — CREATE TABLE (boş doğar) + ADD COLUMN
        new Migration097_RequestItemVehicles(),   // 2026-10-10 kullanıcı isteği: talep kaleminde birden fazla araç — yalnız ADD COLUMN
        new Migration098_RentalVehicles(),        // 2026-10-10 kullanıcı isteği: kiralık araçlar + araç değişimi — yalnız ADD COLUMN
        new Migration099_SyncTxid(),           // 2026-10-17 AR-GE M-03: sunucu işlem kimliği ufku (çekme imleci) — yalnız ekleme
        new Migration100_LocalChangeSeq(),     // 2026-10-17 AR-GE M-03 adım 3: masaüstü gönderme imleci = yerel değişiklik sayacı
        new Migration101_SyncRejections(),     // 2026-10-17 AR-GE M-15: masaüstü gönderilemeyen kayıtlar listesi (yalnız SQLite)
        new Migration102_MachineCode(),        // 2026-10-17 AR-GE M-78/K-21: bilgisayar kodu (belge numarasına gömülür) — yalnız ekleme
        new Migration103_AuditPush(),          // 2026-10-17 AR-GE M-06: masaüstü işlem geçmişi sunucuya (yalnız gönderim) — yalnız ekleme
        new Migration104_TokenEpoch(),         // 2026-10-18 AR-GE M-10: oturum dönemi (şifre değişince eski jetonlar düşer) — yalnız ekleme
        new Migration105_StockDocPurchaseOrder(),   // 2026-10-18 AR-GE M-32: mal kabul belgesi ↔ sipariş bağı — yalnız ekleme
        new Migration106_PeriodLock(),         // 2026-10-18 AR-GE M-30: dönem kilidi (ay kapanışı) — yalnız ekleme
        new Migration107_InvoiceReceiptLink(), // 2026-10-18 AR-GE M-25: fatura ↔ mal kabul bağı — yalnız ekleme
        new Migration108_StockDocRequest(),    // 2026-10-18 AR-GE M-26: talepten stok gönderimi bağı — yalnız ekleme
        new Migration109_TransferInTransit(),  // 2026-10-18 AR-GE M-27: iki adımlı transfer (yolda) — yalnız ekleme
        new Migration110_StockCountDrafts(),   // 2026-10-18 AR-GE M-28: sayım taslağı + fark toleransı — yalnız ekleme
        new Migration111_WorkOrderFault(),     // 2026-10-19 AR-GE M-35: arıza → iş emri bağı — yalnız ekleme
    };
}
