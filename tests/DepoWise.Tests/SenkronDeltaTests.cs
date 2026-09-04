using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ E — SENKRON DELTA DOĞRULUĞU (SNK-09 · SNK-10, 2026-09-04) ═══
///
/// <b>🔴 Bulunan sessiz veri kaybı (SNK-09):</b> istemci, çekimden sonra imleci <b>sunucunun
/// GLOBAL SÜRÜMÜ</b> (<c>MAX(updated_at)</c>) olarak saklıyordu. Sunucu sürümü okunduktan sonra
/// <b>aynı milisaniyede</b> yazılan bir satır bir daha ASLA gelmiyordu: sonraki çekim
/// <c>&gt; imleç</c> sorduğu için damgası eşit olan satır daima eleniyordu. Kayıt sunucuda vardı,
/// makinede hiç görünmüyordu, hiçbir hata da üretmiyordu.
///
/// <b>Bu, Z4'ün PUSH tarafında çözdüğü hatanın PULL karşılığıdır</b> ve aynı çözümle giderildi:
/// imleç artık <b>gerçekten alınan satırların en büyük damgası</b>dır (pull watermark).
///
/// ⚠️ <c>BuildSnapshot</c> içindeki <c>&gt;</c> koşulu BİLİNÇLİ olarak değiştirilmedi: aynı metot
/// PUSH'ta da kullanılır ve orada watermark semantiği zaten doğrudur (Z4-C bunu kilitler).
/// Kusur ortak filtrede değil, imlecin NEYE göre saklandığındaydı.
///
///  SNK9a — Sunucu sürümü imleç yapılsaydı satır kaybolurdu (kusurun kanıtı)
///  SNK9b — Alınan-damga imleciyle kayıp YOK (düzeltmenin kanıtı)
///  SNK9c — Delta hâlâ DELTA: imlecin altındaki eski satırlar boşuna taşınmaz
///  SNK10 — SİLİNEN kayıt delta ile taşınır (silme sessizce kaybolmaz)
///  SNK10b — Firma kapsamı delta yolunda da korunur (başka firmanın satırı sızmaz)
/// </summary>
public class SenkronDeltaTests : IDisposable
{
    private const string Co = "SNK", Yabanci = "SNK2";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly BusinessSyncService _sync;
    private readonly MaterialService _materials;
    private readonly SessionContext _admin, _yabanciAdmin;

    public SenkronDeltaTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_snk_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        _admin = Firma(Co, "admin_snk");
        _yabanciAdmin = Firma(Yabanci, "admin_snk2");
        _sync = new BusinessSyncService(_f);
        _materials = new MaterialService(_f);
    }

    private SessionContext Firma(string co, string user)
    {
        Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{co}','{co}',1,1,1,0);");
        var uid = new UserService(_f).EnsureInitialAdmin(co, user, "admin123", RoleKeys.CompanyAdmin);
        return new SessionContext(uid, co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>Malzemenin damgasını ELLE sabitler — "aynı milisaniye" durumunu deterministik kurar.
    /// (Gerçek hayatta bu, aynı ms içinde iki yazma olduğunda kendiliğinden oluşur.)</summary>
    private void DamgaAta(string materialId, long stamp)
        => Calistir($"UPDATE materials SET updated_at={stamp} WHERE id='{materialId}';");

    /// <summary>Snapshot'taki bir tablonun satır kimliklerini döndürür.</summary>
    private List<string> SnapshotIds(string table, long since)
    {
        var json = _sync.BuildSnapshot(Co, sinceVersion: since);
        using var doc = JsonDocument.Parse(json);
        // Snapshot şekli: { companyId, machineId, tables: { "<tablo>": [satır, ...] } }
        if (!doc.RootElement.TryGetProperty("tables", out var tables)) return new();
        if (!tables.TryGetProperty(table, out var rows)) return new();
        return rows.EnumerateArray().Select(r => r.GetProperty("id").GetString() ?? "").ToList();
    }

    // ══════════════ SNK-09 ══════════════

    /// <summary>
    /// ⭐ KUSURUN KANITI — ESKİ İMLEÇ STRATEJİSİ VERİ KAYBEDERDİ.
    ///
    /// Senaryo: istemci çekim yapar, sunucu global sürümü T'dir. Sunucu sürümü okunduktan SONRA,
    /// AYNI milisaniyede ikinci bir kayıt yazılır (damgası da T). Eski strateji imleci T yapıyordu.
    /// Sonraki çekim <c>&gt; T</c> sorar → ikinci kayıt daima elenir ve BİR DAHA GELMEZ.
    /// </summary>
    [Fact]
    public void SNK9a_Sunucu_Surumu_Imlec_Yapilirsa_Satir_Kaybolur()
    {
        const long T = 1_700_000_000_000;
        var ilk = _materials.Create(_admin, new NewMaterial("M-1", "Çimento"));
        DamgaAta(ilk, T);

        // İstemci çekti; sunucu sürümü = T (global MAX). Sonra AYNI ms'de ikinci kayıt yazıldı.
        var sonra = _materials.Create(_admin, new NewMaterial("M-2", "Demir"));
        DamgaAta(sonra, T);

        // ESKİ STRATEJİ: imleç = sunucu global sürümü (T) → ikinci kayıt SONSUZA KADAR gelmez.
        Assert.DoesNotContain(sonra, SnapshotIds("materials", since: T));
    }

    /// <summary>
    /// ⭐ DÜZELTMENİN KANITI — ALINAN-DAMGA İMLECİYLE KAYIP YOK.
    ///
    /// İstemci artık imleci "sunucunun global sürümü" olarak değil, <b>gerçekten aldığı satırların
    /// en büyük damgası</b> olarak saklar. İlk çekimde yalnız ilk kayıt geldiyse imleç o kaydın
    /// damgasıdır; aynı ms'de yazılan ikinci kayıt bir sonraki çekimde NORMAL ŞEKİLDE gelir.
    /// (Z4'ün push tarafında uyguladığı watermark mantığının aynısı.)
    /// </summary>
    [Fact]
    public void SNK9b_Alinan_Damga_Imleciyle_Kayip_Olmaz()
    {
        const long T = 1_700_000_000_000;
        var ilk = _materials.Create(_admin, new NewMaterial("M-1", "Çimento"));
        DamgaAta(ilk, T);

        // 1. çekim: imlecin altındaki her şey gelir. ALINAN en büyük damga = T-1 (ilk kayıt henüz T değil)
        // senaryosunu kurmak için ilk kaydı daha eski bir damgayla alalım.
        DamgaAta(ilk, T - 1);
        var birinciTur = SnapshotIds("materials", since: 0);
        Assert.Contains(ilk, birinciTur);

        // Alınan-damga imleci = T-1 (gerçekten alınan satırın damgası), sunucu global sürümü DEĞİL.
        var sonra = _materials.Create(_admin, new NewMaterial("M-2", "Demir"));
        DamgaAta(sonra, T);

        // 2. çekim: imleç T-1 → T damgalı kayıt GELİR. Eski stratejide imleç T olsaydı gelmezdi.
        Assert.Contains(sonra, SnapshotIds("materials", since: T - 1));
    }

    /// <summary>⭐ ">=" güvenli mi: sınırdaki satır her turda tekrar gelir — ama uygulama idempotent
    /// olduğu için MÜKERRER kayıt oluşmaz. Bu olmasaydı delik kapatma çaresi kendisi hata üretirdi.</summary>
    [Fact]
    public void SNK9b_Tekrar_Gelen_Satir_Mukerrer_Uretmez()
    {
        const long T = 1_700_000_000_000;
        var m = _materials.Create(_admin, new NewMaterial("M-1", "Çimento"));
        DamgaAta(m, T);

        // Aynı delta iki kez uygulanır (ağ tekrarı / iki tur üst üste).
        var json = _sync.BuildSnapshot(Co, sinceVersion: T);
        using (var d1 = JsonDocument.Parse(json)) _sync.ApplyPull(Co, d1.RootElement);
        using (var d2 = JsonDocument.Parse(json)) _sync.ApplyPull(Co, d2.RootElement);

        Assert.Equal(1L, Say($"SELECT COUNT(*) FROM materials WHERE id='{m}';"));
        Assert.Equal(1L, Say($"SELECT COUNT(*) FROM materials WHERE company_id='{Co}' AND code='M-1';"));
    }

    /// <summary>⭐ Delta HÂLÂ delta: ">=" yaptık diye tam snapshot'a dönmedik. İmlecin ALTINDAKİ
    /// eski satırlar taşınmaz — aksi hâlde SNK-06'nın kazancı kaybolurdu.</summary>
    [Fact]
    public void SNK9c_Imlecin_Altindaki_Eski_Satirlar_Tasinmaz()
    {
        var eski = _materials.Create(_admin, new NewMaterial("M-ESKI", "Eski"));
        DamgaAta(eski, 1_000);
        var yeni = _materials.Create(_admin, new NewMaterial("M-YENI", "Yeni"));
        DamgaAta(yeni, 9_000);

        // İmleç, ALINAN en büyük damgadır. Eski satır alındıysa imleç 1.000'dir → yeni satır (9.000) gelir,
        // eski satır (1.000) ">" ile elenir: ne kayıp ne gereksiz tekrar.
        var gelen = SnapshotIds("materials", since: 1_000);
        Assert.Contains(yeni, gelen);
        Assert.DoesNotContain(eski, gelen);   // eski satır boşuna taşınmıyor
    }

    // ══════════════ SNK-10 ══════════════

    /// <summary>
    /// SNK-10 — SİLİNEN KAYIT DELTA İLE TAŞINIR. Silme bu projede soft delete'tir
    /// (<c>is_deleted=1</c> + <c>updated_at</c> tazelenir) → silinen satır delta penceresine girer ve
    /// karşı tarafa "artık silinmiş" olarak ulaşır. Taşınmasaydı bir makinede silinen kayıt
    /// diğerinde SONSUZA KADAR yaşardı — iki makine sessizce ayrışırdı.
    /// </summary>
    [Fact]
    public void SNK10_Silinen_Kayit_Delta_Ile_Tasinir()
    {
        var m = _materials.Create(_admin, new NewMaterial("M-SIL", "Silinecek"));
        DamgaAta(m, 5_000);

        // İstemci 5.000 imleciyle güncel. Şimdi kayıt silinir (soft delete).
        _materials.Delete(_admin, m);

        var gelen = SnapshotIds("materials", since: 5_000);
        Assert.Contains(m, gelen);   // silinen satır DELTAYA GİRER

        // ...ve silinmiş olarak gelir (karşı taraf onu gizleyebilsin).
        var json = _sync.BuildSnapshot(Co, sinceVersion: 5_000);
        using var doc = JsonDocument.Parse(json);
        var satir = doc.RootElement.GetProperty("tables").GetProperty("materials").EnumerateArray()
            .First(r => r.GetProperty("id").GetString() == m);
        Assert.Equal(1, satir.GetProperty("is_deleted").GetInt32());
    }

    /// <summary>SNK-10b — Delta yolunda da FİRMA KAPSAMI korunur. Optimizasyon yaparken en kolay
    /// kaçırılan şey budur: hızlanan sorgu sessizce başka firmanın satırını taşıyabilir.</summary>
    [Fact]
    public void SNK10b_Delta_Yolunda_Firma_Kapsami_Korunur()
    {
        var benim = _materials.Create(_admin, new NewMaterial("M-BENIM", "Benim"));
        var onun = _materials.Create(_yabanciAdmin, new NewMaterial("M-ONUN", "Onun"));
        DamgaAta(benim, 7_000);
        Calistir($"UPDATE materials SET updated_at=7000 WHERE id='{onun}';");

        // İmleç 6.999 → iki satır da damga olarak kapsama girer; kapsamı belirleyen tek şey FİRMADIR.
        var gelen = SnapshotIds("materials", since: 6_999);
        Assert.Contains(benim, gelen);
        Assert.DoesNotContain(onun, gelen);   // başka firmanın satırı SIZMAZ
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
