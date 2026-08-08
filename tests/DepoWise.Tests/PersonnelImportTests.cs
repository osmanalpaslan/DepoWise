using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DepoWise.Application.Common;
using DepoWise.Application.Reports;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Reporting;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// PERSONEL içe aktarımı (kullanıcı isteği 2026-07-16: "toplu personel listesini de içeri almak istiyorum;
/// saha personeli veya kullanıcı ise sütunda nasıl belirtmem gerek").
///
/// İki kavramın Excel karşılığı:
///  • "Saha Personeli" = Evet → uygulamaya GİRMEZ.
///  • "Kullanıcı Adı"        → uygulamaya GİRER; MEVCUT hesap bağlanır (import hesap AÇMAZ).
/// İkisi birbirini dışlar (ekranda da öyle: kutucuk işaretlenince kullanıcı bağı temizlenir).
///
/// Hacim: kullanıcının dosyası ~2600 satır → testler 3000 ile çalışır.
/// </summary>
public class PersonnelImportTests : IDisposable
{
    private const int VolumeRows = 3000;

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly TestClock _clock = new();
    private readonly PersonnelService _personnel;
    private readonly PersonnelTitleService _titles;
    private readonly UserService _users;
    private readonly LookupService _lookups;
    private readonly DepoWise.Infrastructure.Organization.BranchService _branches;
    private readonly PersonnelImportService _imp;
    private readonly SessionContext _admin;

    public PersonnelImportTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "depowise_pimp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        _personnel = new PersonnelService(_factory, new ScopeResolver(_factory), _clock);
        _titles = new PersonnelTitleService(_factory, _clock);
        _users = new UserService(_factory, _clock);
        _lookups = new LookupService(_factory, _clock);
        _branches = new DepoWise.Infrastructure.Organization.BranchService(_factory, _clock);
        _imp = new PersonnelImportService(_personnel, _titles, _users, _lookups);
        var uid = _users.EnsureInitialAdmin("A", "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, "A", new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        // 2026-08-09: içe aktarma artık Şube/Şantiye OLUŞTURMAZ → testte önceden tanımlanır.
        _branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez Şantiye", "site", null, null, null));
        for (int i = 0; i < 8; i++)
            _branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch($"Şantiye-{i}", "site", null, null, null));
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
    }

    private static ImportRow Row(int n, params (string Col, string? Val)[] cells)
        => new(n, cells.ToDictionary(c => c.Col, c => c.Val));

    private List<PersonnelRecord> AllPersonnel()
    {
        var list = new List<PersonnelRecord>();
        string? cursor = null;
        do
        {
            var page = _personnel.List(_admin, new PageRequest { Limit = PageRequest.MaxLimit, Cursor = cursor });
            list.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return list;
    }

    /// <summary>Kullanıcı hesabı açar (Kullanıcılar ekranının yaptığı iş) — import bunu YAPMAZ.</summary>
    private string CreateUser(string username)
    {
        var branch = _branches.List(_admin).FirstOrDefault()?.Id ?? _branches.Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        return _users.CreateUser(_admin, new NewUser(username, "p12345", username,
            new[] { RoleKeys.Staff }, CompanyId: "A", BranchId: branch));
    }

    // ══════════════ ŞABLON ══════════════
    [Fact]
    public void Sablon_FormdakiTumAlanlariIcerir()
    {
        var h = _imp.SampleHeaders();
        foreach (var expected in new[] { "Ad Soyad", "Unvan", "Telefon", "Şube", "Aktif", "Saha Personeli", "Kullanıcı Adı" })
            Assert.Contains(expected, h);
    }

    // ══════════════ TEMEL AKTARIM ══════════════
    [Fact]
    public void TumAlanlar_DoluAktarilir_TanimlarOtomatikOlusur()
    {
        var (res, created) = _imp.CommitWithLookups(_admin, new[]
        {
            Row(2, ("Ad Soyad", "Ahmet Yılmaz"), ("Unvan", "Şoför"), ("Telefon", "0555 111 22 33"),
                   ("Şube", "Merkez Şantiye"), ("Aktif", "Evet"), ("Saha Personeli", "Evet")),
        });

        Assert.Equal(1, res.Added);
        Assert.Equal(0, res.Failed);

        var p = AllPersonnel().Single();
        Assert.Equal("Ahmet Yılmaz", p.FullName);
        Assert.Equal("Şoför", p.Title);
        Assert.Equal("0555 111 22 33", p.Phone);
        Assert.True(p.IsActive);
        Assert.True(p.IsFieldStaff);
        Assert.NotNull(p.BranchId);

        Assert.Contains(created, x => x.Contains("Şoför"));
        // 2026-08-09: Şube/Şantiye artık içe aktarmada OLUŞTURULMAZ.
        Assert.DoesNotContain(created, x => x.Contains("Merkez Şantiye"));
    }

    [Fact]
    public void AdSoyad_Zorunlu()
    {
        var dry = _imp.DryRun(_admin, new[] { Row(2, ("Ad Soyad", ""), ("Unvan", "Şoför")) });
        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("Ad Soyad zorunlu"));
    }

    /// <summary>Aktif boş → varsayılan EVET (form da öyle: yeni personel aktif başlar).</summary>
    [Fact]
    public void Aktif_BosIse_VarsayilanEvet()
    {
        _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "Ali Veli")) });
        Assert.True(AllPersonnel().Single().IsActive);
    }

    /// <summary>Saha Personeli boş → varsayılan HAYIR (form da işaretsiz başlar).</summary>
    [Fact]
    public void SahaPersoneli_BosIse_VarsayilanHayir()
    {
        _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "Ali Veli")) });
        Assert.False(AllPersonnel().Single().IsFieldStaff);
    }

    // ══════════════ Evet/Hayır yazım varyasyonları ══════════════
    [Theory]
    [InlineData("Evet", true)]
    [InlineData("evet", true)]
    [InlineData("E", true)]
    [InlineData("VAR", true)]
    [InlineData("X", true)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("Hayır", false)]
    [InlineData("hayir", false)]
    [InlineData("H", false)]
    [InlineData("Yok", false)]
    [InlineData("0", false)]
    [InlineData("false", false)]
    public void SahaPersoneli_YazimVaryasyonlari_DogruOkunur(string text, bool expected)
    {
        _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "Ali Veli"), ("Saha Personeli", text)) });
        Assert.Equal(expected, AllPersonnel().Single().IsFieldStaff);
    }

    /// <summary>Tanınmayan değer SESSİZCE "hayır" sayılmamalı — satır reddedilmeli (yanlış veri üretmesin).</summary>
    [Fact]
    public void SahaPersoneli_TaninmayanDeger_SatirReddedilir()
    {
        var dry = _imp.DryRun(_admin, new[] { Row(2, ("Ad Soyad", "Ali"), ("Saha Personeli", "belki")) });
        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("Evet ya da Hayır"));
    }

    // ══════════════ KULLANICI BAĞLAMA ══════════════
    [Fact]
    public void KullaniciAdi_MevcutHesabiBaglar()
    {
        var uid = CreateUser("ahmet.y");

        var res = _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "Ahmet Yılmaz"), ("Kullanıcı Adı", "ahmet.y")) });

        Assert.Equal(1, res.Added);
        var p = AllPersonnel().Single();
        var accounts = _users.AccountsByPersonnel("A");
        Assert.True(accounts.ContainsKey(p.Id));
        Assert.Equal(uid, accounts[p.Id].UserId);
        Assert.Equal("ahmet.y", accounts[p.Id].Username);
    }

    /// <summary>İçe aktarım hesap AÇMAZ: olmayan kullanıcı adı → satır net mesajla reddedilir.</summary>
    [Fact]
    public void KullaniciAdi_OlmayanHesap_SatirReddedilir_HesapACILMAZ()
    {
        var dry = _imp.DryRun(_admin, new[] { Row(2, ("Ad Soyad", "Ahmet"), ("Kullanıcı Adı", "yok.boyle.kullanici")) });

        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("Kullanıcı bulunamadı") && e.Message.Contains("hesap AÇMAZ"));
    }

    /// <summary>ÇELİŞKİ: saha personeli uygulamaya girmez → kullanıcı bağlanamaz.</summary>
    [Fact]
    public void SahaPersoneliVeKullaniciAdi_Birlikte_Celiski_Reddedilir()
    {
        CreateUser("ahmet.y");

        var dry = _imp.DryRun(_admin, new[]
        {
            Row(2, ("Ad Soyad", "Ahmet"), ("Saha Personeli", "Evet"), ("Kullanıcı Adı", "ahmet.y")),
        });

        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("Çelişki"));
    }

    /// <summary>Bir personele TEK hesap: aynı kullanıcı adı iki satırda → ikincisi reddedilir
    /// (ilk satır hesabı bağladı, hesap artık bağlanabilir listesinde değil).</summary>
    [Fact]
    public void AyniKullaniciAdi_IkiSatirda_IkincisiReddedilir()
    {
        CreateUser("ortak.hesap");

        var res = _imp.Commit(_admin, new[]
        {
            Row(2, ("Ad Soyad", "Ahmet Yılmaz"), ("Kullanıcı Adı", "ortak.hesap")),
            Row(3, ("Ad Soyad", "Mehmet Demir"), ("Kullanıcı Adı", "ortak.hesap")),
        });

        Assert.Equal(1, res.Added);
        Assert.Equal(1, res.Failed);
    }

    /// <summary>Zaten başka personele bağlı hesap yeniden bağlanamaz (bağlanabilir listesinde değildir).</summary>
    [Fact]
    public void ZatenBagliHesap_TekrarBaglanamaz()
    {
        var uid = CreateUser("bagli.hesap");
        var pid = _personnel.Create(_admin, new NewPersonnel("Eski Kişi", null, null, null));
        _users.LinkPersonnel(_admin, uid, pid);

        var dry = _imp.DryRun(_admin, new[] { Row(2, ("Ad Soyad", "Yeni Kişi"), ("Kullanıcı Adı", "bagli.hesap")) });

        Assert.Equal(0, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("bağlanamaz"));
    }

    /// <summary>Kullanıcı adı büyük/küçük harf duyarsız eşlenir (Excel'de "AHMET.Y" yazılmış olabilir).</summary>
    [Fact]
    public void KullaniciAdi_HarfDuyarsizEslesir()
    {
        CreateUser("ahmet.y");
        var res = _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "Ahmet"), ("Kullanıcı Adı", "AHMET.Y")) });
        Assert.Equal(1, res.Added);
    }

    // ══════════════ UNVAN (sabit tanım) ══════════════
    /// <summary>Unvan tanımı Türkçe duyarlı tekilleşir: "Şoför" ve "şoför" TEK tanım.</summary>
    [Fact]
    public void Unvan_TurkceDuyarliTekilleşir()
    {
        _imp.Commit(_admin, new[]
        {
            Row(2, ("Ad Soyad", "A"), ("Unvan", "Şoför")),
            Row(3, ("Ad Soyad", "B"), ("Unvan", "şoför")),
            Row(4, ("Ad Soyad", "C"), ("Unvan", "ŞOFÖR")),
        });

        Assert.Single(_titles.List(_admin));
    }

    [Fact]
    public void Unvan_MevcutTanimVarsa_YenisiOlusmaz()
    {
        _titles.Create(_admin, "Operatör");

        var (_, created) = _imp.CommitWithLookups(_admin, new[] { Row(2, ("Ad Soyad", "A"), ("Unvan", "Operatör")) });

        Assert.Single(_titles.List(_admin));
        Assert.DoesNotContain(created, x => x.Contains("Unvan"));
    }

    // ══════════════ MÜKERRER ══════════════
    /// <summary>Aynı dosya iki kez → personel TEKRARLANMAZ (mükerrer anahtarı: normalize ad).</summary>
    [Fact]
    public void AyniDosyaIkiKez_Tekrarlanmaz()
    {
        var rows = new[]
        {
            Row(2, ("Ad Soyad", "Ahmet Yılmaz"), ("Unvan", "Şoför")),
            Row(3, ("Ad Soyad", "Mehmet Demir")),
        };

        var first = _imp.Commit(_admin, rows);
        var second = _imp.Commit(_admin, rows);

        Assert.Equal(2, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(2, second.Updated);           // "zaten vardı, atlandı"
        Assert.Equal(2, AllPersonnel().Count);
        Assert.Single(_titles.List(_admin));       // unvan da tekrar oluşmadı
    }

    /// <summary>Mükerrer eşleme boşluk/harf duyarsız: "AHMET YILMAZ" = "Ahmet Yılmaz".</summary>
    [Fact]
    public void MukerrerEsleme_BoslukVeHarfDuyarsiz()
    {
        _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "Ahmet Yılmaz")) });
        var second = _imp.Commit(_admin, new[] { Row(2, ("Ad Soyad", "  ahmet  yılmaz ")) });

        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Updated);
        Assert.Single(AllPersonnel());
    }

    [Fact]
    public void DosyaIcindeTekrarEdenAd_DryRunYakalar()
    {
        var dry = _imp.DryRun(_admin, new[]
        {
            Row(2, ("Ad Soyad", "Ahmet Yılmaz")),
            Row(3, ("Ad Soyad", "ahmet yılmaz")),
        });

        Assert.Equal(1, dry.Valid);
        Assert.Contains(dry.Errors, e => e.Message.Contains("birden çok kez"));
    }

    // ══════════════ YETKİ ══════════════
    [Fact]
    public void Yetkisiz_AktarimYapamaz()
    {
        var uid = _users.EnsureInitialAdmin("A", "personel", "p12345", RoleKeys.Staff);
        var staff = new SessionContext(uid, "A", new[] { RoleKeys.Staff }, PermissionSet.Empty);

        Assert.Throws<ForbiddenException>(() => _imp.Commit(staff, new[] { Row(2, ("Ad Soyad", "X")) }));
    }

    // ══════════════ HACİM (kullanıcının dosyası ~2600) ══════════════
    [Fact]
    public void Hacim_3000Personel_MakulSuredeAktarilir()
    {
        var rows = new List<ImportRow>(VolumeRows);
        for (int i = 0; i < VolumeRows; i++)
        {
            rows.Add(Row(i + 2,
                ("Ad Soyad", $"Personel {i:D5}"),
                ("Unvan", $"Unvan-{i % 12}"),
                ("Telefon", $"0555{i:D7}"),
                ("Şube", $"Şantiye-{i % 8}"),
                ("Aktif", i % 5 == 0 ? "Hayır" : "Evet"),
                ("Saha Personeli", i % 3 == 0 ? "Evet" : "Hayır")));
        }

        var sw = Stopwatch.StartNew();
        var (res, created) = _imp.CommitWithLookups(_admin, rows);
        sw.Stop();

        Assert.Equal(VolumeRows, res.Added);
        Assert.Equal(0, res.Failed);
        Assert.Equal(VolumeRows, AllPersonnel().Count);

        // Tanımlar TEKİL: 12 unvan + 8 şantiye.
        Assert.Equal(12, _titles.List(_admin).Count);
        // 12 unvan; 8 şantiye ARTIK oluşturulmuyor (önceden tanımlı) → yalnız 12.
        Assert.Equal(12, created.Count);
        Assert.True(sw.Elapsed < TimeSpan.FromMinutes(3), $"3000 personel {sw.Elapsed.TotalSeconds:0} sn sürdü — çok yavaş (önbellek bozulmuş olabilir).");
    }

    /// <summary>⚠️ REGRESYON: PersonnelService.List (PageRequest) 200 ile SINIRLIDIR. Mükerrer kontrolü buna
    /// dayansaydı 201. kişiden sonrası "yok" sanılıp KOPYA oluşurdu (AllNameToId sayfalamasızdır).</summary>
    [Fact]
    public void Regresyon_250Personel_IkinciAktarimda_200SonrasiKopyalanmaz()
    {
        var rows = Enumerable.Range(0, 250)
            .Select(i => Row(i + 2, ("Ad Soyad", $"Personel {i:D4}")))
            .ToList();

        _imp.Commit(_admin, rows);
        var second = _imp.Commit(_admin, rows);

        Assert.Equal(0, second.Added);
        Assert.Equal(250, second.Updated);
        Assert.Equal(250, AllPersonnel().Count);
    }

    /// <summary>3000 satırda bozuk satırlar karışık: sağlamlar girer, bozuklar atlanır, hata listesi şişmez.</summary>
    [Fact]
    public void Hacim_3000Personel_BozukSatirlarKarisik_SaglamlarGirer()
    {
        var rows = new List<ImportRow>(VolumeRows);
        int bad = 0;
        for (int i = 0; i < VolumeRows; i++)
        {
            if (i % 10 == 0) { rows.Add(Row(i + 2, ("Ad Soyad", ""))); bad++; }                                  // ad yok
            else if (i % 10 == 1) { rows.Add(Row(i + 2, ("Ad Soyad", $"P{i}"), ("Aktif", "belki"))); bad++; }    // geçersiz Evet/Hayır
            else rows.Add(Row(i + 2, ("Ad Soyad", $"Personel {i:D5}")));
        }

        var res = _imp.Commit(_admin, rows);

        Assert.Equal(VolumeRows - bad, res.Added);
        Assert.Equal(bad, res.Failed);
        Assert.True(res.Errors.Count <= ImportResult.MaxReportedErrors);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }
}
