using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AR-GE 2026-10-26 — NÖBETÇİ: FİRMA VERİSİ TAŞIYAN HER TABLONUN EŞİTLEME KARARI VAR ═══
/// B6'da altı tablo eşitleme listesine eklenmeyi UNUTMUŞTU (Migration121 ile düzeltildi): masaüstü kayıtları sunucuya hiç gitmiyordu.
/// Bu test, şemadaki <c>company_id</c> taşıyan her tablonun ya eşitleme listesinde ya da aşağıda GEREKÇESİYLE "eşitlenmez" diye
/// işaretli olmasını zorlar. Yeni tablo ekleyen kişi karar vermeden test geçmez.
///  EK1 — her firma tablosu: eşitlenir ya da gerekçeli eşitlenmez
///  EK2 — listeler çakışmaz ve eşitlenmez listesinde artık var olmayan tablo kalmaz
/// </summary>
public class EsitlemeKarariTests : IDisposable
{
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "dw_esk_" + Guid.NewGuid().ToString("N") + ".db");

    public void Dispose() { try { File.Delete(_yol); } catch { } }

    /// <summary>Bilinçli olarak İŞ EŞİTLEMESİNE girmeyen firma tabloları — her biri gerekçeli.</summary>
    public static readonly IReadOnlyDictionary<string, string> Eslenmez = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["stock_count_drafts"] = "sayım taslağı: sayımı yapan bilgisayardaki yarım iş (Migration121 kararı)",
        ["stock_balances"] = "türetilmiş bakiye: her tarafta defterden (stock_movements) yeniden hesaplanır",
        ["file_records"] = "fotoğraf/dosya künyesi: ikili içerikle birlikte ayrı yükleme yolundan gider (DesktopPhotos / OrgServerClient)",
        // ── Sunucu otoriteli, AYRI eşitleme yoluyla masaüstüne inen tanım/yetki tabloları (CompanySyncService / LookupSyncService) ──
        ["branches"] = "şube/şantiye: sunucu otoriteli, org eşitlemesiyle ayna olarak iner (kod/şifre taşır)",
        ["users"] = "kullanıcı hesapları: sunucu otoriteli, org eşitlemesiyle iner (parola özeti iş paketine girmez)",
        ["roles"] = "roller: sunucu otoriteli yetki tanımı, org eşitlemesiyle iner",
        ["role_permissions"] = "rol yetkileri: sunucu otoriteli, org eşitlemesiyle iner",
        ["role_button_permissions"] = "rol düğme yetkileri: sunucu otoriteli, org eşitlemesiyle iner",
        ["role_grant_limits"] = "rol yetki tavanı: sunucu otoriteli, org eşitlemesiyle iner",
        ["user_permissions"] = "kullanıcı yetkileri: sunucu otoriteli, org eşitlemesiyle iner",
        ["user_button_permissions"] = "kullanıcı düğme yetkileri: sunucu otoriteli, org eşitlemesiyle iner",
        ["user_scopes"] = "kullanıcı şube kapsamı: sunucu otoriteli, org eşitlemesiyle iner",
        ["user_hierarchy"] = "kullanıcı hiyerarşisi: sunucu otoriteli, tanım eşitlemesiyle (LookupSyncService) iner",
        ["company_grant_limits"] = "firma yetki tavanı: yalnız süper yönetici, sunucu otoriteli",
        ["permission_templates"] = "yetki şablonları: yalnız sunucuda yönetilir (web/çevrimiçi ekran)",
        ["field_protections"] = "alan koruma kuralları: sunucu otoriteli yetki ayarı, girişte oturumla iner",
        ["field_requirements"] = "firma zorunlu alanları: tanım eşitlemesiyle (LookupSyncService) iner",
        ["screen_platform_visibility"] = "ekran görünürlüğü: tanım eşitlemesiyle (LookupSyncService) iner",
        ["screen_menu_layout"] = "menü düzeni: tanım eşitlemesiyle (LookupSyncService) iner",
        ["menu_group_layout"] = "menü grup düzeni: tanım eşitlemesiyle (LookupSyncService) iner",
        ["teams"] = "ekipler: tanım eşitlemesiyle (LookupSyncService) iner",
        ["team_members"] = "ekip üyeleri: tanım eşitlemesiyle (LookupSyncService) iner",
        ["fx_rates"] = "döviz kurları: tanım eşitlemesiyle (LookupSyncService) iner",
        // ── Masaüstünde de ÇEVRİMİÇİ çalışan ekranlar (yerelde tutulmaz → eşitleme gerekmez) ──
        ["projects"] = "projeler: masaüstü ekranı doğrudan sunucuya bağlanır (OrgServerClient), yerele yazılmaz",
        ["project_branches"] = "proje-şube bağı: projelerle birlikte çevrimiçi",
        ["approval_instance"] = "onay merkezi: çevrimiçi (OnlineApprovalClient), karar sunucuda verilir",
        ["approval_step"] = "onay adımları: onay merkeziyle birlikte çevrimiçi",
        ["chat_messages"] = "sohbet: çevrimiçi (API), yerele yazılmaz",
        // ── Kişisel / bilgisayara özel ──
        ["alert_reads"] = "uyarı 'okundu' işareti: kişinin o ekrandaki okuma izi; iş verisi değil",
        ["app_settings"] = "bilgisayar/firma uygulama ayarları (sunucu adresi, tema, eşitleme imleçleri)",
        // ── Eşitleme, oturum ve güvenlik iç tabloları ──
        ["sync_devices"] = "eşitleme iç tablosu: kayıtlı bilgisayarlar (sunucu)",
        ["sync_inbox"] = "eşitleme iç tablosu: eski op-id gelen kutusu",
        ["sync_outbox"] = "eşitleme iç tablosu: eski op-id giden kutusu",
        ["sync_conflicts"] = "eşitleme iç tablosu: çakışma kayıtları",
        ["sync_rejections"] = "eşitleme iç tablosu: gönderim redleri (bilgisayar başına)",
        ["data_conflicts"] = "eşitleme iç tablosu: veri çakışmaları (yönetici kararı sunucuda)",
        ["server_changes"] = "eşitleme iç tablosu: sunucu değişiklik günlüğü",
        ["company_local_resets"] = "bakım iç tablosu: 'yereli sıfırla' istekleri",
        ["company_purges"] = "bakım iç tablosu: kalıcı silme kayıtları (yalnız süper yönetici)",
        ["enrollment_keys"] = "güvenlik: bilgisayar kayıt anahtarları (sunucu)",
        ["sessions"] = "güvenlik: oturumlar (sunucu)",
        ["remember_tokens"] = "güvenlik: 'beni hatırla' jetonları (sunucu)",
        ["login_attempts"] = "güvenlik: giriş denemeleri / kilit (sunucu)",
    };

    private IReadOnlyList<string> FirmaTablolari()
    {
        var f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(f).Run();
        using var conn = f.Create();
        var tablolar = new List<string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";
            using var r = cmd.ExecuteReader();
            while (r.Read()) tablolar.Add(r.GetString(0));
        }
        return tablolar.Where(t => DbIntrospect.ColumnExists(conn, null, t, "company_id")).ToList();
    }

    [Fact]
    public void EK1_Her_Firma_Tablosunun_Eslesme_Karari_Var()
    {
        var kararsiz = FirmaTablolari()
            .Where(t => !BusinessSyncService.Tables.Contains(t) && !Eslenmez.ContainsKey(t)).ToList();
        Assert.True(kararsiz.Count == 0,
            "Eşitleme kararı verilmemiş firma tabloları (BusinessSyncService.Tables'a ekleyin ya da EsitlemeKarariTests.Eslenmez'e " +
            "GEREKÇESİYLE yazın):\n" + string.Join("\n", kararsiz));
    }

    [Fact]
    public void EK2_Listeler_Tutarli()
    {
        var firma = FirmaTablolari().ToHashSet(StringComparer.Ordinal);
        Assert.Empty(Eslenmez.Keys.Where(t => BusinessSyncService.Tables.Contains(t)));       // hem eşitlenir hem eşitlenmez olamaz
        Assert.Empty(Eslenmez.Keys.Where(t => !firma.Contains(t)));                            // kaldırılmış tablo listede kalmasın
        Assert.All(Eslenmez.Values, g => Assert.True(g.Length >= 15, "gerekçe yazılmalı"));
    }
}
