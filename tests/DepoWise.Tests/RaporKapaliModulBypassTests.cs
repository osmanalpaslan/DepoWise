using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Reporting;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ RPR-15 · "ROL YETKİ KONTROL" İLE KAPATILAN EKRANIN VERİSİ RAPORDAN OKUNABİLİYOR ═══
/// (denetim 2026-08-26, dördüncü tur)
///
/// <b>Belgelenmiş güvence</b> (<c>RoleGrantService</c> sınıf açıklaması): süper adminin bir ROLE
/// kapattığı modül için <i>"oturum yüklenirken izin satırı DÜŞÜRÜLÜR → <b>admin bypass'ı dahil API/UI
/// erişimi kapanır</b>"</i>. Yani "Rol Yetki Kontrol" ekranında bir ekranı bir role kapatmak,
/// o rolün o veriye <b>hiçbir yoldan</b> ulaşamaması demektir.
///
/// <b>Bulunan durum.</b> Rapor servisindeki kapı yalnız <c>reports</c> modülünü sorar
/// (<c>AccessControl.Require(s, "reports", View)</c>). Raporun OKUDUĞU ekranın (stok, yakıt, araç,
/// bakım, talep) kapalı olup olmadığına <b>bakmaz</b>. Sonuç: süper admin "Stok" ekranını Personel
/// rolüne kapatsa bile, o roldeki kullanıcı <b>Stok Hareketleri raporunu çalıştırıp aynı veriyi
/// satır satır okuyabiliyordu</b> — hatta Excel'e aktarabiliyordu.
///
/// ⚠️ Bu bir <b>tenant/şube açığı değildir</b> (firma ve şube kapsamı doğru uygulanıyor); ihlal edilen
/// şey <b>rol bazlı ekran kapatma</b> güvencesidir.
///
/// <b>Ayrım (önemli).</b> Sekiz rapor (Muayene, Personel ve 6 ön muhasebe) ilgili modülün TAM iznini
/// zaten ister (RPR-12). Kalan 13 rapor için <b>tam izin istemek</b>, bugün yalnız "Raporlar" yetkisi
/// verilmiş kullanıcıların erişimini KESERDİ. Bu yüzden düzeltme dar tutulmuştur: yalnız
/// <b>açıkça KAPATILMIŞ</b> (blocked) modülün verisi engellenir. Kimsenin mevcut erişimi kesilmez.
/// </summary>
public class RaporKapaliModulBypassTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"dw_rpr15_{Guid.NewGuid():N}.db");
    private readonly SqliteConnectionFactory _factory;
    private readonly ReportService _reports;
    private const string Co = "RPR15-CO";

    private sealed class SabitSaat : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    public RaporKapaliModulBypassTests()
    {
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _reports = new ReportService(_factory, new SabitSaat());

        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        Sql($"INSERT INTO materials(id,company_id,code,name,unit_id,min_stock,created_at,updated_at,version,is_deleted) " +
            $"VALUES('M1','{Co}','KOD-1','GIZLI CIMENTO',NULL,'0',1,1,1,0);");
        // Rapor satırı üretecek bir stok hareketi.
        Sql($"INSERT INTO stock_movements(id,company_id,material_id,movement_type,direction,quantity,operation_id,created_at) " +
            $"VALUES('MV1','{Co}','M1','in',1,'42','op-1',1699000000000);");
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Yalnız "Raporlar" yetkisi olan personel; <paramref name="kapali"/> modüller role kapatılmış.</summary>
    private static SessionContext Personel(params string[] kapali)
        => new("u1", Co, new[] { RoleKeys.Staff },
               new PermissionSet(new[]
               {
                   new ModulePermission("reports", CanView: true, CanCreate: false, CanEdit: false, CanDelete: false),
               }))
        {
            BlockedModules = new HashSet<string>(kapali, StringComparer.Ordinal),
        };

    private static ReportRequest Istek() => new(Executed: true, FromDate: 1, ToDate: 1_800_000_000_000);

    // ── 1) Kapatma gerçekten çalışıyor mu (kontrol) ────────────────────────────────────────────

    [Fact]
    public void RPR15_Kapatilan_Ekran_Dogrudan_Erisime_Kapali()
    {
        var s = Personel("stock");
        Assert.False(AccessControl.Can(s, "stock", PermissionAction.View));
    }

    // ── 2) ⭐ ASIL BULGU: kapalı ekranın verisi rapordan okunabiliyor mu ───────────────────────

    /// <summary>⭐ RPR-15 — "Stok" ekranı role KAPALIYKEN stok hareketleri raporu ÇALIŞMAMALI.</summary>
    [Fact]
    public void RPR15a_Kapali_Stok_Ekraninin_Verisi_Rapordan_OKUNAMAZ()
    {
        var s = Personel("stock");
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, "stock-movements", Istek()));
    }

    /// <summary>⭐ RPR-15 — içerik kanıtı: kapalıyken malzeme ADI hiçbir satırda görünmemeli.</summary>
    [Fact]
    public void RPR15b_Kapali_Iken_Gizli_Veri_Satirda_Gorunmez()
    {
        var s = Personel("stock");

        string metin;
        try
        {
            var t = _reports.Run(s, "stock-movements", Istek());
            metin = string.Join("|", t.Rows.SelectMany(r => r.Select(c => c?.ToString() ?? "")));
        }
        catch (ForbiddenException) { metin = ""; }   // beklenen: hiç veri üretilmemesi

        Assert.DoesNotContain("GIZLI CIMENTO", metin, StringComparison.Ordinal);
    }

    /// <summary>Aynı kural stok sayım ve stok durumu raporlarında da geçerli (aynı ekranın verisi).</summary>
    [Theory]
    [InlineData("stock")]
    [InlineData("stock-count")]
    public void RPR15c_Kapali_Stok_Diger_Stok_Raporlarini_Da_Kapatir(string rapor)
    {
        var s = Personel("stock");
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, rapor, Istek()));
    }

    // ── 3) REGRESYON KİLİTLERİ: kimsenin mevcut erişimi KESİLMEMELİ ───────────────────────────

    /// <summary>⭐ Kritik kilit: hiçbir modül kapatılmamışsa, yalnız "Raporlar" yetkisi YETMEYE devam eder.</summary>
    [Fact]
    public void RPR15d_Kapatma_Yoksa_Yalniz_Raporlar_Yetkisi_YETER()
    {
        var s = Personel();   // hiçbir modül kapalı değil
        var t = _reports.Run(s, "stock-movements", Istek());

        Assert.NotNull(t);
        Assert.Contains("GIZLI CIMENTO",
            string.Join("|", t.Rows.SelectMany(r => r.Select(c => c?.ToString() ?? ""))), StringComparison.Ordinal);
    }

    /// <summary>Kilit: BAŞKA bir modülün kapatılması stok raporunu etkilemez (aşırı kapatma yok).</summary>
    [Fact]
    public void RPR15e_Ilgisiz_Modulun_Kapatilmasi_Raporu_Etkilemez()
    {
        var s = Personel("fuel", "vehicles");
        var t = _reports.Run(s, "stock-movements", Istek());

        Assert.Contains("GIZLI CIMENTO",
            string.Join("|", t.Rows.SelectMany(r => r.Select(c => c?.ToString() ?? ""))), StringComparison.Ordinal);
    }

    /// <summary>Kilit: SÜPER ADMİN rol kapatmasından muaftır (platform sahibi kendini kilitlemez).</summary>
    [Fact]
    public void RPR15f_Super_Admin_Muaf_Kalir()
    {
        var s = new SessionContext("su", Co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty)
        {
            BlockedModules = new HashSet<string>(new[] { "stock" }, StringComparer.Ordinal),
        };

        var t = _reports.Run(s, "stock-movements", Istek());
        Assert.NotNull(t);
    }

    /// <summary>Kilit: "reports" yetkisi hiç yoksa davranış eskisi gibi reddetmeye devam eder.</summary>
    [Fact]
    public void RPR15g_Raporlar_Yetkisi_Yoksa_Yine_Reddedilir()
    {
        var s = new SessionContext("u2", Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => _reports.Run(s, "stock-movements", Istek()));
    }

    // ── 4) WEB / MASAÜSTÜ PARİTESİ (rapor LİSTESİ görünürlüğü) ────────────────────────────────
    //
    // Servis kapısı (ReportService.Run) tek noktadır ve iki platformu birden korur. Ama rapor
    // LİSTESİ iki ayrı yerde süzülür (web katalog ucu + masaüstü ReportsViewModel). Biri düzeltilip
    // diğeri unutulursa kullanıcı çalışmayacak bir raporu listede görür. Bu yüzden ikisi de kilitlenir.

    private static string KaynakOku(params string[] parcalar)
    {
        var kok = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && kok is not null; i++)
        {
            var aday = Path.Combine(new[] { kok }.Concat(parcalar).ToArray());
            if (File.Exists(aday)) return File.ReadAllText(aday);
            kok = Path.GetDirectoryName(kok!);
        }
        throw new FileNotFoundException("Kaynak bulunamadı: " + string.Join("/", parcalar));
    }

    [Theory]
    [InlineData("src", "DepoWise.Api", "Program.cs")]
    [InlineData("src", "DepoWise.Desktop", "ViewModels", "ReportsViewModel.cs")]
    public void RPR15h_Rapor_Listesi_Iki_Platformda_Da_Kapali_Modulu_Gizler(params string[] yol)
    {
        var src = KaynakOku(yol);

        Assert.Contains("DataModule", src, StringComparison.Ordinal);
        Assert.Contains("BlockedModules.Contains", src, StringComparison.Ordinal);
    }

    /// <summary>Katalog bütünlüğü: her raporun ya tam izin modülü ya veri evi olmalı — ya da bilinçli istisna.</summary>
    [Fact]
    public void RPR15i_Katalogda_Sahipsiz_Rapor_Kalmadi()
    {
        // Tek bilinçli istisna: "Durum Rapor" firma geneli ÇAPRAZ-MODÜL sayısal özettir; tek bir
        // "veri evi" yoktur. Yeni bir rapor sessizce sahipsiz kalırsa bu test uyarır.
        var istisnalar = new[] { "status" };

        var sahipsiz = ReportCatalog.All
            .Where(d => d.RequiredModule is null && d.DataModule is null && !istisnalar.Contains(d.Key))
            .Select(d => d.Key)
            .ToList();

        Assert.True(sahipsiz.Count == 0, "Modülü tanımlanmamış rapor(lar): " + string.Join(", ", sahipsiz));
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var ext in new[] { "", "-wal", "-shm" })
        {
            var p = _dbPath + ext;
            if (File.Exists(p)) { try { File.Delete(p); } catch { } }
        }
    }
}
