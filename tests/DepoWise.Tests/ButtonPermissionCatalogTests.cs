using System.Text.RegularExpressions;
using DepoWise.Application.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ YET-01/02 · ÖZEL BUTON YETKİLERİ İLE GERÇEK KOD ARASINDAKİ TUTARLILIK ═══ (denetim 2026-08-26)
///
/// Özel butonlar iki listede yaşar:
/// <list type="bullet">
///   <item><b>Yetki ağacı</b> (<see cref="SpecialButtons.All"/>) — yöneticinin verebildikleri.</item>
///   <item><b>Kod</b> — <c>RequireButton</c> / <c>CanUseButton</c> ile gerçekten kapı olanlar.</item>
/// </list>
///
/// İki liste ayrışırsa iki ayrı arıza doğar ve ikisi de sessizdir:
/// <list type="number">
///   <item><b>YET-02 (bulundu, düzeltildi):</b> kodda kapı var ama ağaçta YOK → yetki yalnız admin
///     bypass'ıyla geçilir; yönetici kimseye veremez, kullanıcı çıkmaza girer. <c>btn-reverse</c> tam
///     olarak buydu (stok ters kaydı + iki yakıt iptali).</item>
///   <item><b>YET-01 (bulundu, raporlandı):</b> ağaçta var ama kodda hiçbir yerde kapı DEĞİL → yönetici
///     bir yetki verdiğini sanır, hiçbir şey olmaz. <c>btn-reset-db</c> ve <c>btn-logo</c> böyledir.
///     Anahtarları SİLMEK verilmiş kayıtları öksüz bırakacağı için bu turda dokunulmadı; bilinçli
///     istisna olarak aşağıda listelenir ki YENİ bir işlevsiz buton sessizce eklenemesin.</item>
/// </list>
/// </summary>
public class ButtonPermissionCatalogTests
{
    /// <summary>
    /// Ağaçta duran ama kodda kapı OLMAYAN, BİLİNEN ve kabul edilmiş anahtarlar (YET-01).
    /// Yeni bir anahtar buraya eklenmeden işlevsiz kalamaz — test kırılır.
    /// </summary>
    private static readonly string[] BilinenIslevsizler = { "btn-reset-db", "btn-logo" };

    private static string RepoKok()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new InvalidOperationException("DepoWise.sln bulunamadı.");
    }

    /// <summary>Kaynakta <c>RequireButton/CanUseButton(..., SpecialButtons.X)</c> ile kapı kurulan adlar.</summary>
    private static HashSet<string> KoddaKapiOlanlar()
    {
        var kok = RepoKok();
        var adlar = new HashSet<string>(StringComparer.Ordinal);
        // ⚠ Kalip esletirme DAR olamaz: gercek kodda cagri cok satirli bir ternary icinde gecebiliyor
        // (or. RequireButton(s, IsManagerReport(type) ? ExportManagerReports : ExportReports)). Ilk surum
        // tam bunu kacirdi ve iki disa aktarma butonunu "islevsiz" sandi. Bu yuzden kural basitlestirildi:
        // anahtar, katalog dosyasi DISINDA herhangi bir yerde geciyorsa "kodda karsiligi var" sayilir.
        var re = new Regex(@"SpecialButtons.([A-Za-z]+)");

        foreach (var f in Directory.EnumerateFiles(Path.Combine(kok, "src"), "*.*", SearchOption.AllDirectories))
        {
            if (!f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
                !f.EndsWith(".razor", StringComparison.OrdinalIgnoreCase)) continue;
            if (f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (f.EndsWith("AppModules.cs", StringComparison.Ordinal)) continue;   // katalogun kendisi sayilmaz

            foreach (Match m in re.Matches(File.ReadAllText(f)))
            {
                var ad = m.Groups[1].Value;
                if (ad == nameof(SpecialButtons.All)) continue;   // listenin kendisi bir buton degil
                adlar.Add(ad);
            }
        }
        return adlar;
    }

    /// <summary>
    /// Sabit adı → anahtar değeri (ör. <c>Reverse</c> → <c>btn-reverse</c>).
    ///
    /// ⚠️ Eskiden elle yazılmış bir <c>switch</c>'ti ve YENİ BUTON EKLENDİĞİNDE güncellenmesi
    /// gerekiyordu; unutulduğunda ad çözülemiyor, buton "kodda kapısı yok" sanılıyor ve test
    /// GERÇEK OLMAYAN bir hata veriyordu (TRH-01/LOG-01'de tam olarak bu oldu). Artık değer
    /// <see cref="SpecialButtons"/> üzerinden YANSIMAYLA okunur → liste kendini bakar, sapma olamaz.
    /// </summary>
    private static string AnahtarFor(string sabitAdi)
        => typeof(SpecialButtons).GetField(sabitAdi,
               System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
               ?.GetRawConstantValue() as string
           ?? sabitAdi;

    /// <summary>⭐ YET-02 — kodda kapı olan HER buton yetki ağacında da olmalı (yoksa devredilemez).</summary>
    [Fact]
    public void YET02_Kodda_Kapi_Olan_Her_Buton_Agacta_Var()
    {
        var agac = SpecialButtons.All.Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var eksik = new List<string>();

        foreach (var ad in KoddaKapiOlanlar())
        {
            var anahtar = AnahtarFor(ad);
            // Approve LEGACY'dir: artık bir MODÜL (request_approval), buton olarak kullanılmaz.
            if (anahtar == SpecialButtons.Approve) continue;
            if (!agac.Contains(anahtar)) eksik.Add($"{ad} ({anahtar})");
        }

        Assert.True(eksik.Count == 0,
            "Kodda kapı olan ama yetki ağacında BULUNMAYAN buton(lar) var → yalnız admin geçebilir, " +
            "yönetici kimseye veremez ve kullanıcı çıkmaza girer:\n  " + string.Join("\n  ", eksik));
    }

    /// <summary>⭐ YET-01 — ağaçtaki her buton kodda gerçekten kapı olmalı (bilinen istisnalar hariç).</summary>
    [Fact]
    public void YET01_Agactaki_Her_Buton_Kodda_Kapi()
    {
        var kodda = KoddaKapiOlanlar().Select(AnahtarFor).ToHashSet(StringComparer.Ordinal);
        var islevsiz = SpecialButtons.All
            .Select(x => x.Key)
            .Where(k => !kodda.Contains(k))
            .Where(k => !BilinenIslevsizler.Contains(k, StringComparer.Ordinal))
            .ToList();

        Assert.True(islevsiz.Count == 0,
            "Yetki ağacında görünen ama kodda HİÇBİR yerde kapı OLMAYAN buton(lar) var → yönetici yetki " +
            "verdiğini sanır, hiçbir şey değişmez:\n  " + string.Join("\n  ", islevsiz) +
            "\nBilinçli bir istisnaysa BilinenIslevsizler listesine gerekçesiyle ekleyin.");
    }

    /// <summary>Bilinen istisnalar GERÇEKTEN hâlâ işlevsiz mi? Biri kapıya bağlanırsa liste küçülmeli.</summary>
    [Fact]
    public void YET01_Bilinen_Istisnalar_Hala_Gecerli()
    {
        var kodda = KoddaKapiOlanlar().Select(AnahtarFor).ToHashSet(StringComparer.Ordinal);

        foreach (var k in BilinenIslevsizler)
            Assert.False(kodda.Contains(k),
                $"{k} artık kodda kapı olarak kullanılıyor → BilinenIslevsizler listesinden çıkarın.");
    }

    /// <summary>KİLİT: ters kayıt/iptal yetkisi artık VERİLEBİLİR (YET-02 düzeltmesinin özü).</summary>
    [Fact]
    public void YET02_Ters_Kayit_Yetkisi_Verilebilir()
    {
        Assert.Contains(SpecialButtons.All, x => x.Key == SpecialButtons.Reverse);

        // Yetki verilmemiş personel geçemez; verilmiş personel geçer; admin zaten geçer.
        var personel = new SessionContext("p", "CO", new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.False(AccessControl.CanUseButton(personel, SpecialButtons.Reverse));

        var yetkili = new SessionContext("p2", "CO", new[] { RoleKeys.Staff },
            new PermissionSet(Array.Empty<ModulePermission>(), new[] { SpecialButtons.Reverse }));
        Assert.True(AccessControl.CanUseButton(yetkili, SpecialButtons.Reverse));

        var admin = new SessionContext("a", "CO", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        Assert.True(AccessControl.CanUseButton(admin, SpecialButtons.Reverse));
    }
}
