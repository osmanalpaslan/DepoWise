using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ SIF-03 · SIFIRLAMA BİLDİRİMİ SESSİZCE YUTULUYORDU ═══ (denetim 2026-08-26)
///
/// <c>/api/admin/reset-company-business</c> önce sunucudaki iş verisini SİLİYOR, sonra makinelere
/// "yerel kopyanı temizle" isteği bırakıyordu — ve bu ikinci adım <b>boş bir catch</b> ile yutuluyordu.
///
/// <b>Neden tehlikeli:</b> ikinci adım başarısız olursa sunucu boşalmış ama masaüstleri bunu hiç
/// öğrenmemiş olur; bir sonraki gönderimde silinen veriyi geri yüklerler. Bu, SIF-02'de kapatılan
/// "silinen veri geri geliyor" hatasının aynısıdır. Üstelik yanıt yine <c>ok: true</c> dönüyordu.
///
/// <b>Düzeltme:</b> sıra tersine çevrildi — ÖNCE bildirim, SONRA silme. Bildirim yıkıcı değildir
/// ("yereli temizle + sunucudan yeniden çek"), bu yüzden silme sonradan başarısız olsa bile veri kaybı
/// olmaz. Bildirim başarısız olursa hiçbir şey silinmez ve kullanıcı hatayı GÖRÜR.
///
/// Uç yıkıcı olduğu için kural <b>kaynak kilidi</b> ile korunur (çalıştırmak gerçek bir firmayı silmeyi
/// gerektirirdi). <see cref="SIF03_Kural_Gercekten_Yakaliyor_Mu"/> kuralın kendisini sınar: ilk sürümde
/// bölge, gerçek uç kaydı yerine bir YORUM satırındaki aynı metinden başlıyordu ve kural yanlış bloğu
/// ölçüyordu — kasten bozma denemesiyle yakalandı, çapa kesinleştirildi.
/// </summary>
public class CompanyResetNotifyTests
{
    private const string Anchor = "app.MapPost(\"/api/admin/reset-company-business\"";
    private const string AnchorSon = "app.MapPost(\"/api/admin/company-local-reset\"";

    private static string Kaynak()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            var p = Path.Combine(dir, "src", "DepoWise.Api", "Program.cs");
            if (File.Exists(p)) return File.ReadAllText(p);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("Program.cs bulunamadı.");
    }

    /// <summary>Uç gövdesini ÇAPA ile keser — yorum içindeki aynı metin bölgeyi kaydırmasın diye
    /// <c>app.MapPost("…")</c> kaydının kendisi aranır.</summary>
    private static string UcGovdesi(string kaynak)
    {
        var bas = kaynak.IndexOf(Anchor, StringComparison.Ordinal);
        Assert.True(bas >= 0, "reset-company-business uç KAYDI bulunamadı");
        var son = kaynak.IndexOf(AnchorSon, bas, StringComparison.Ordinal);
        Assert.True(son > bas, "ucun sonu bulunamadı");
        return kaynak.Substring(bas, son - bas);
    }

    /// <summary>Kuralın kendisi: bildirim silmeden ÖNCE mi? (true = doğru sıra)</summary>
    private static bool SiraDogru(string ucGovdesi)
    {
        var bildirim = ucGovdesi.IndexOf("CompanyLocalReset.RequestReset", StringComparison.Ordinal);
        var silme = ucGovdesi.IndexOf("CompanyPurge.ResetBusinessData", StringComparison.Ordinal);
        return bildirim > 0 && silme > 0 && bildirim < silme;
    }

    private static bool Yutuyor(string ucGovdesi)
        => ucGovdesi.Contains("RequestReset(s, companyId).RequestedAt; } catch { }", StringComparison.Ordinal);

    // ── gerçek kod ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SIF03_Bildirim_Yutulmuyor()
    {
        var uc = UcGovdesi(Kaynak());
        Assert.False(Yutuyor(uc), "Bildirim hâlâ boş catch ile yutuluyor.");
        Assert.Contains("HİÇBİR ŞEY SİLİNMEDİ", uc);   // hata kullanıcıya AÇIKÇA dönüyor
    }

    [Fact]
    public void SIF03_Bildirim_Silmeden_ONCE_Yapiliyor()
    {
        var uc = UcGovdesi(Kaynak());
        Assert.True(SiraDogru(uc),
            "Bildirim SİLMEDEN ÖNCE yapılmalı: aksi halde bildirim başarısız olursa sunucu boşalır ama " +
            "makineler silinen veriyi geri yükler (SIF-02'nin tekrarı).");
    }

    /// <summary>KİLİT: kardeş uç (kalıcı silme) da bildirimi yutmamalı.</summary>
    [Fact]
    public void SIF03_Kalici_Silme_Ucunda_Bos_Catch_Yok()
    {
        var s = Kaynak();
        var bas = s.IndexOf("app.MapPost(\"/api/admin/purge-company\"", StringComparison.Ordinal);
        Assert.True(bas > 0, "purge-company uç kaydı bulunamadı");
        var son = s.IndexOf(Anchor, bas, StringComparison.Ordinal);
        Assert.True(son > bas);

        Assert.False(Yutuyor(s.Substring(bas, son - bas)));
    }

    /// <summary>
    /// ⭐ Kural gerçekten yakalıyor mu? Kasten YANLIŞ bir gövde üretilir ve tespit edilmesi beklenir —
    /// test "her zaman yeşil" bir kabuk olmasın. (İlk sürüm tam da bu yüzden sessizce geçiyordu.)
    /// </summary>
    [Fact]
    public void SIF03_Kural_Gercekten_Yakaliyor_Mu()
    {
        const string kotu =
            "app.MapPost(\"/api/admin/reset-company-business\", (x) => {\n" +
            "    res = svc.CompanyPurge.ResetBusinessData(s, companyId);\n" +
            "    try { resetAt = svc.CompanyLocalReset.RequestReset(s, companyId).RequestedAt; } catch { }\n" +
            "});\n" +
            "app.MapPost(\"/api/admin/company-local-reset\", (y) => { });\n";

        const string iyi =
            "app.MapPost(\"/api/admin/reset-company-business\", (x) => {\n" +
            "    resetAt = svc.CompanyLocalReset.RequestReset(s, companyId).RequestedAt;\n" +
            "    res = svc.CompanyPurge.ResetBusinessData(s, companyId);\n" +
            "});\n" +
            "app.MapPost(\"/api/admin/company-local-reset\", (y) => { });\n";

        Assert.False(SiraDogru(UcGovdesi(kotu)), "yanlış sıra yakalanmadı");
        Assert.True(Yutuyor(UcGovdesi(kotu)), "yutulan catch yakalanmadı");

        Assert.True(SiraDogru(UcGovdesi(iyi)), "doğru sıra yanlış pozitif verdi");
        Assert.False(Yutuyor(UcGovdesi(iyi)), "doğru gövdede yutma sanıldı");
    }
}
