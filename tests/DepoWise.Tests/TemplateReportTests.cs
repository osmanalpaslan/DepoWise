using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// Yönetici raporları (2026-07-24): ŞABLONLU / ŞABLON-DIŞI ayrımı. Şablon SEÇİLEREK oluşturulan malzeme/araç
/// "şablonlu"; şablonsuz oluşturulan "şablon-dışı". Rapor ikisini doğru ayırmalı + tenant-izole olmalı.
/// </summary>
public class TemplateReportTests : IDisposable
{
    private readonly string _db;
    private readonly SqliteConnectionFactory _f;
    private readonly TestClock _clock = new();

    public TemplateReportTests()
    {
        _db = Path.Combine(Path.GetTempPath(), "dw_tplrep_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_db);
        new MigrationRunner(_f).Run();
    }
    private sealed class TestClock : IClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000); }

    private SessionContext Admin(string co)
    {
        var u = new UserService(_f, _clock);
        var id = u.EnsureInitialAdmin(co, "adm_" + co, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(id, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    [Fact]
    public void Sablonlu_SablonDisi_Malzeme_Ve_Arac_Raporlari_Ayrisir()
    {
        var a = Admin("A");
        var mats = new MaterialService(_f, _clock);
        var matTpl = new MaterialTemplateService(_f, _clock);
        var veh = new VehicleService(_f, _clock);
        var vehTpl = new VehicleTemplateService(_f, _clock);
        var reports = new ReportService(_f);
        var req = new ReportRequest(Executed: true);

        // MALZEME — 1 şablon; 1 şablonlu + 1 şablonsuz
        var mt = matTpl.Create(a, new NewMaterialTemplate("Yağ Filtresi Şb", Code: "YF-T"));
        mats.Create(a, new NewMaterial("M-T", "Yağ Filtresi", TemplateId: mt));
        var mnId = mats.Create(a, new NewMaterial("M-N", "Serbest Malzeme"));   // şablonsuz → template_id NULL

        var mByT = reports.MaterialsByTemplate(a, req);
        Assert.Equal(2, mByT.Rows.Count);                 // 1 şablon satırı + TOPLAM
        Assert.Equal("YF-T", mByT.Rows[0][0]);            // şablon kodu
        Assert.Equal(1, Convert.ToInt32(mByT.Rows[0][2])); // kayıt sayısı

        var mNon = reports.MaterialsNonTemplate(a, req);
        Assert.Single(mNon.Rows);
        Assert.Equal("M-N", mNon.Rows[0][0]);

        // DÜZENLEMEDE BAĞLAMA: şablon-dışı M-N'yi şablona bağla → şablonluya taşınmalı, şablon-dışından çıkmalı.
        mats.Update(a, mnId, new UpdateMaterial("M-N", "Serbest Malzeme", TemplateId: mt));
        Assert.Empty(reports.MaterialsNonTemplate(a, req).Rows);                  // artık şablon-dışı yok
        Assert.Equal(2, Convert.ToInt32(reports.MaterialsByTemplate(a, req).Rows[0][2])); // şablonda artık 2 kayıt

        // ARAÇ — 1 şablon; 1 şablonlu + 1 şablonsuz
        var vt = vehTpl.Create(a, new NewVehicleTemplate("Ekskavatör Şb"));
        veh.Create(a, new NewVehicle("V-T", TemplateId: vt));
        veh.Create(a, new NewVehicle("V-N"));             // şablonsuz

        var vByT = reports.VehiclesByTemplate(a, req);
        Assert.Single(vByT.Rows);
        Assert.Equal("V-T", vByT.Rows[0][1]);            // (0=şablon, 1=iç kod)

        var vNon = reports.VehiclesNonTemplate(a, req);
        Assert.Single(vNon.Rows);
        Assert.Equal("V-N", vNon.Rows[0][0]);
    }

    /// <summary>
    /// G2-04 (PRT-01 Grup 2, 2026-08-10) — HIZLI DÜZENLEME ŞABLON BAĞINI SİLMEMELİ.
    ///
    /// Kök neden: <see cref="MaterialService.Update"/> "template_id=@tpl" ile KOŞULSUZ yazar ve
    /// <see cref="UpdateMaterial.TemplateId"/> varsayılanı <c>null</c>'dır. Web hızlı düzenleme penceresi
    /// (MaterialEditDialog) ve masaüstü hızlı düzenleme penceresi (MaterialQuickEditWindow) şablonu
    /// DEĞİŞTİRMEZ — ama bağı geri göndermedikleri için her kaydetmede sessizce NULL'a düşürüyorlardı.
    /// Kolon gerçek rapor besliyor (MaterialsByTemplate / MaterialsNonTemplate) → sessiz veri kaybıydı.
    ///
    /// Test iki yönü birlikte kanıtlar: (1) bağ geri gönderilirse KORUNUR — hızlı düzenlemenin bugünkü
    /// davranışı; (2) gönderilmezse KAYBOLUR — bulgunun kök nedeni, regresyon nöbetçisi olarak sabitlenir.
    /// Doğrulama yalnız kolonda değil, sonucun göründüğü RAPORDA da yapılır.
    /// </summary>
    [Fact]
    public void HizliDuzenleme_SablonBagi_GeriGonderilirse_KORUNUR_gonderilmezse_KAYBOLUR()
    {
        var a = Admin("G204");
        var mats = new MaterialService(_f, _clock);
        var matTpl = new MaterialTemplateService(_f, _clock);
        var reports = new ReportService(_f);
        var req = new ReportRequest(Executed: true);

        var tpl = matTpl.Create(a, new NewMaterialTemplate("Hidrolik Hortum Şb", Code: "HH-T"));
        var id = mats.Create(a, new NewMaterial("M-HQ", "Hidrolik Hortum", TemplateId: tpl));
        Assert.Equal(tpl, mats.GetDetail(a, id).TemplateId);   // başlangıç: şablona bağlı

        // (1) HIZLI DÜZENLEME — pencerenin bugün gönderdiği çağrı: yüklenen alanlar + ŞABLON BAĞI + sürüm.
        var d = mats.GetDetail(a, id);
        mats.Update(a, id, new UpdateMaterial(
            Code: d.Code, Name: "Hidrolik Hortum 2", Type: d.Type,
            CategoryId: d.CategoryId, UnitId: d.UnitId, BrandId: d.BrandId, SupplierId: d.SupplierId,
            MinStock: d.MinStock, UnitPrice: d.UnitPrice, Description: d.Description,
            TemplateId: d.TemplateId), expectedVersion: d.Version);

        var after = mats.GetDetail(a, id);
        Assert.Equal("Hidrolik Hortum 2", after.Name);   // düzenleme gerçekten uygulandı
        Assert.Equal(tpl, after.TemplateId);             // ŞABLON BAĞI KORUNDU
        Assert.Empty(reports.MaterialsNonTemplate(a, req).Rows);                          // "şablon-dışı"na düşmedi
        Assert.Equal(1, Convert.ToInt32(reports.MaterialsByTemplate(a, req).Rows[0][2])); // şablonda hâlâ 1 kayıt

        // (2) REGRESYON NÖBETÇİSİ — bağ gönderilmezse kolon NULL'a düşer (düzeltilen davranışın kanıtı).
        var d2 = mats.GetDetail(a, id);
        mats.Update(a, id, new UpdateMaterial(Code: d2.Code, Name: d2.Name), expectedVersion: d2.Version);
        Assert.Null(mats.GetDetail(a, id).TemplateId);
        Assert.Single(reports.MaterialsNonTemplate(a, req).Rows);   // rapora "şablon-dışı" olarak düştü
    }

    public void Dispose() { try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_db); } catch { } }
}
