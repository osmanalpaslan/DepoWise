using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ FAZ 3b-3 + 3b-4 (ADR-223, kullanıcı onayı 2026-09-05) — ALAN BAZLI YETKİ ═══
///
/// <b>En önemli kabul kriteri (kullanıcının şartı):</b>
/// <i>"CANLI VERİ VAR. ÇALIŞAN HİÇBİR ŞEY BOZULMAYACAK."</i> → AL1 bunu doğrudan ölçer:
/// <c>field_protections</c> boşken hiçbir alan gizlenmez, hiçbir davranış değişmez.
///
/// <b>İkinci şart:</b> <i>"sadece UI'da buton gizleyerek 'yetki sistemi yaptım' deme"</i> →
/// testlerin tamamı SERVİS katmanını çağırır; hiçbiri arayüze bakmaz.
///
///  AL1  — Koruma tablosu BOŞKEN davranış bugünküyle BİREBİR aynı
///  AL2  — Korumalı alan, izinsiz kullanıcıya kapalı (kart + liste + grid)
///  AL3  — Açık <c>fld_</c> izni alanı AÇAR
///  AL4  — EDIT ⇒ VIEW: göremediği alanı düzenleyemez (view=0, edit=1 geçersiz)
///  AL5  — 🔴 VERİ KAYBI YOK: alanı göremeyen kullanıcı kaydı güncellerse fiyat KORUNUR
///  AL6  — Görünür ama düzenlenemez + DEĞİŞTİRİLMİŞ değer → 403
///  AL7  — Görünür ama düzenlenemez + AYNI değer → geçer (ekran kullanılamaz hâle gelmez)
///  AL8  — Firma admini korumalı alanı görür (admin bypass, bilinçli)
///  AL9  — 🔴 ÇIKARIM KANALI: gizli alanın FİLTRESİ yok sayılır
///  AL10 — 🔴 ÇIKARIM KANALI: gizli alana göre SIRALAMA düşer
///  AL11 — 🔴 ÇIKARIM KANALI: gizli fiyattan türeyen "stok değeri" özeti hesaplanmaz
///  AL12 — Dışa aktarımda kolon BAŞLIĞIYLA BİRLİKTE düşer
///  AL13 — TENANT: A firmasının koruması B firmasını etkilemez
///  AL14 — ROL üzerinden verilen <c>fld_</c> izni çalışır (Faz 3a birleşimi bedava)
///  AL15 — Katalog dışı alan korunamaz (fail-closed)
///  AL16 — Koruma ayarı yetki gerektirir
///  AL17 — Ön muhasebe: cari borç/alacak/bakiye gizlenir
///  AL18 — Ön muhasebe: fatura tutarı gizlenir; detay fail-closed
///  AL19 — Ön muhasebe: kasa/banka tutarı ve yürüyen bakiye gizlenir
///  AL20 — Yeni kayıtta gizli alan gönderilse de yazılmaz
///  AL21 — Koruma değişince yetki fotoğrafı düşer (bayat karar yok)
///  AL22 — PERFORMANS: 10.000 satırda karar SORGU başına bir kez (satır başına DB yok)
///  AL23 — 🔴 RAPOR KAÇAĞI: ön muhasebe raporları da alan kapısından geçer
/// </summary>
public class AlanYetkisiTests : IDisposable
{
    private const string Co = "ALN";
    private const string CoB = "ALN-B";
    private const string Pass = "Alan!2026";

    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly AuthService _auth;
    private readonly PermissionService _perms;
    private readonly FieldProtectionService _koruma;
    private readonly PermissionSnapshotCache _cache = new();
    private readonly MaterialService _mat;
    private readonly string _personelId;

    public AlanYetkisiTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_alan_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        foreach (var c in new[] { Co, CoB })
            Calistir($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{c}','{c}',1,1,1,0);");

        var users = new UserService(_f);
        users.EnsureInitialAdmin(Co, "aln_admin", Pass, RoleKeys.CompanyAdmin);
        _personelId = users.EnsureInitialAdmin(Co, "aln_personel", Pass, RoleKeys.Staff);

        _auth = new AuthService(_f, null, _cache);
        _perms = new PermissionService(_f, null, _cache);
        _koruma = new FieldProtectionService(_f, null, _cache);
        _mat = new MaterialService(_f);
    }

    // ── yardımcılar ─────────────────────────────────────────────────────────────────────────

    private void Calistir(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private long Say(string sql)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    private SessionContext Oturum(string kullaniciAdi)
    {
        var r = _auth.Login(Co, kullaniciAdi, Pass);
        Assert.True(r.Success, "Giriş başarısız: " + kullaniciAdi);
        return r.Session!;
    }

    private static SessionContext SuperAdmin(string co = Co)
        => new("sa", co, new[] { RoleKeys.SuperAdmin }, PermissionSet.Empty);

    private static ModulePermission Tam(string modul) => new(modul, true, true, true, true);

    /// <summary>Personele malzeme modülünün tamamını verir (alan yetkisi HARİÇ).</summary>
    private void PersoneleMalzemeYetkisi()
        => _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("materials") }, Array.Empty<string>());

    /// <summary>Birim fiyatı firma genelinde KORUMALI yapar.</summary>
    private void FiyatiKoru()
        => _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, true);

    private string MalzemeOlustur(SessionContext s, string kod, decimal fiyat)
        => _mat.Create(s, new NewMaterial(kod, kod + " adı", UnitPrice: fiyat));

    private decimal HamFiyat(string materialId)
    {
        using var conn = _f.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT unit_price FROM materials WHERE id=@i;";
        cmd.AddWithValue("@i", materialId);
        return Money.Parse(cmd.ExecuteScalar() as string);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(_dbPath); } catch { }
    }

    // ══════════════════ AL1 — EN ÖNEMLİ: BOŞ TABLO = BUGÜNKÜ DAVRANIŞ ══════════════════

    /// <summary>
    /// ⭐ Geriye dönük uyumluluğun tek cümlelik kanıtı. Tablo oluştu ama BOŞ: hiçbir alan korumalı
    /// değil → en dar yetkili kullanıcı bile fiyatı görür ve düzenler. Yayın günü hiçbir ekran
    /// değişmez. Bu test kırılırsa canlı veriye dokunulmuş demektir.
    /// </summary>
    [Fact]
    public void AL1_Koruma_Tablosu_Bosken_Davranis_Aynidir()
    {
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));

        PersoneleMalzemeYetkisi();
        var s = Oturum("aln_personel");

        // Hiçbir fld_ izni YOK — yine de alan açık.
        Assert.True(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.True(FieldAccess.Duzenlenebilir(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.True(MaterialService.FiyatGorunur(s));

        var id = MalzemeOlustur(s, "AL1", 125.50m);
        Assert.Equal(125.50m, _mat.GetDetail(s, id).UnitPrice);
        Assert.Equal(125.50m, HamFiyat(id));

        var grid = _mat.SearchGrid(s, new MaterialGridFilter(), 1, 50);
        Assert.Equal(125.50m, grid.Items.Single(x => x.Code == "AL1").UnitPrice);

        // Özet de eskisi gibi hesaplanır.
        Assert.True(_mat.SearchGridSummary(s, new MaterialGridFilter()).StockValueVisible);
    }

    // ══════════════════ AL2–AL3 — GİZLEME VE AÇMA ══════════════════

    [Fact]
    public void AL2_Korumali_Alan_Izinsiz_Kullaniciya_Kapalidir()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL2", 99.90m);

        FiyatiKoru();
        var s = Oturum("aln_personel");

        Assert.False(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.Equal(0m, _mat.GetDetail(s, id).UnitPrice);
        Assert.Equal(0m, _mat.SearchGrid(s, new MaterialGridFilter(), 1, 50).Items.Single(x => x.Code == "AL2").UnitPrice);
        Assert.Equal(0m, _mat.List(s, new PageRequest { Limit = 50 }).Items.Single(x => x.Code == "AL2").UnitPrice);

        // 🔴 EN ÖNEMLİSİ: gizlenen yalnız GÖRÜNÜMDÜR — veri yerinde durur.
        Assert.Equal(99.90m, HamFiyat(id));
    }

    [Fact]
    public void AL3_Acik_Alan_Izni_Alani_Acar()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL3", 42m);
        FiyatiKoru();

        var anahtar = FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);
        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), new ModulePermission(anahtar, true, false, true, false) },
            Array.Empty<string>());

        var s = Oturum("aln_personel");
        Assert.True(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.True(FieldAccess.Duzenlenebilir(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.Equal(42m, _mat.GetDetail(s, id).UnitPrice);
    }

    // ══════════════════ AL4 — EDIT ⇒ VIEW ══════════════════

    /// <summary>
    /// ⭐ D3 (kullanıcı kararı): göremediği alanı kimse düzenleyemez. Yetki satırında yanlışlıkla
    /// "view=0, edit=1" bulunsa bile etkin sonuç KAPALIDIR — okumadan yazma oluşamaz.
    /// </summary>
    [Fact]
    public void AL4_Duzenleme_Gormeyi_Gerektirir()
    {
        PersoneleMalzemeYetkisi();
        FiyatiKoru();

        var anahtar = FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);

        // (1) FAZ 3b-5: SERVİS bu kombinasyonu artık REDDEDER (yazma yolu kapısı).
        Assert.Throws<ArgumentException>(() =>
            _perms.SaveForUser(SuperAdmin(), _personelId,
                new[] { Tam("materials"), new ModulePermission(anahtar, false, false, true, false) },
                Array.Empty<string>()));

        // (2) Ama arayüz ve servis GÜVENLİĞİN TAMAMI DEĞİLDİR: satır doğrudan VERİTABANINA ekilir
        //     (eski sürümden kalmış ya da elle yazılmış bozuk kayıt senaryosu). ÇALIŞMA ANI kuralı
        //     yine de kazanmalı — "göremediğini düzenleyemez".
        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("materials") }, Array.Empty<string>());
        Calistir("INSERT INTO user_permissions(id, company_id, user_id, module_key, can_view, can_create, can_edit, can_delete, created_at, updated_at, version) " +
                 $"VALUES('bozuk1','{Co}','{_personelId}','{anahtar}',0,0,1,0,1,1,1);");

        var s = Oturum("aln_personel");
        Assert.False(FieldAccess.Gorunur(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));
        Assert.False(FieldAccess.Duzenlenebilir(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice));

        // Kuralın saf hâli de doğrulanır (kayıt yazılmadan önce reddedilebilsin diye).
        Assert.False(FieldAccess.GecerliMi(canView: false, canEdit: true));
        Assert.True(FieldAccess.GecerliMi(canView: true, canEdit: true));
        Assert.True(FieldAccess.GecerliMi(canView: false, canEdit: false));
    }

    // ══════════════════ AL5–AL7 — YAZMA YOLU ══════════════════

    /// <summary>
    /// 🔴 EN KRİTİK VERİ BÜTÜNLÜĞÜ TESTİ. Alanı GÖREMEYEN kullanıcı formda 0 görür; kaydettiğinde
    /// gerçek fiyatın 0'lanması <b>sessiz veri kaybı</b> olurdu. Kayıttaki değer korunmalıdır.
    /// </summary>
    [Fact]
    public void AL5_Gizli_Alan_Guncellemede_Korunur_Veri_Kaybi_Yok()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL5", 777.77m);
        FiyatiKoru();

        var s = Oturum("aln_personel");
        var d = _mat.GetDetail(s, id);
        Assert.Equal(0m, d.UnitPrice);   // kullanıcı 0 görür

        // Kullanıcı YALNIZ adı değiştirir; formdaki 0 fiyat da gönderilir.
        _mat.Update(s, id, new UpdateMaterial(d.Code, "AL5 yeni ad", d.Type, d.CategoryId, d.UnitId,
            d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, d.Description, d.TemplateId));

        Assert.Equal(777.77m, HamFiyat(id));                       // 🔴 fiyat KORUNDU
        Assert.Equal("AL5 yeni ad", _mat.GetDetail(s, id).Name);   // istenen değişiklik yapıldı
    }

    [Fact]
    public void AL6_Gorunur_Ama_Duzenlenemez_Degisiklik_Reddedilir()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL6", 10m);
        FiyatiKoru();

        var anahtar = FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);
        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), new ModulePermission(anahtar, true, false, false, false) },   // yalnız görme
            Array.Empty<string>());

        var s = Oturum("aln_personel");
        var d = _mat.GetDetail(s, id);
        Assert.Equal(10m, d.UnitPrice);

        var ex = Assert.Throws<ForbiddenException>(() =>
            _mat.Update(s, id, new UpdateMaterial(d.Code, d.Name, d.Type, d.CategoryId, d.UnitId,
                d.BrandId, d.SupplierId, d.MinStock, 20m, d.Description, d.TemplateId)));
        Assert.Contains("Birim Fiyat", ex.Message);
        Assert.Equal(10m, HamFiyat(id));   // değişmedi
    }

    /// <summary>Aynı değeri geri göndermek engellenmez — aksi hâlde kullanıcı adı bile değiştiremezdi.</summary>
    [Fact]
    public void AL7_Gorunur_Ama_Duzenlenemez_Ayni_Deger_Gecer()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL7", 33m);
        FiyatiKoru();

        var anahtar = FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);
        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("materials"), new ModulePermission(anahtar, true, false, false, false) },
            Array.Empty<string>());

        var s = Oturum("aln_personel");
        var d = _mat.GetDetail(s, id);
        _mat.Update(s, id, new UpdateMaterial(d.Code, "AL7 yeni", d.Type, d.CategoryId, d.UnitId,
            d.BrandId, d.SupplierId, d.MinStock, d.UnitPrice, d.Description, d.TemplateId));

        Assert.Equal(33m, HamFiyat(id));
        Assert.Equal("AL7 yeni", _mat.GetDetail(s, id).Name);
    }

    // ══════════════════ AL8 — ADMIN ══════════════════

    [Fact]
    public void AL8_Firma_Admini_Korumali_Alani_Gorur()
    {
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL8", 55m);
        FiyatiKoru();

        var yeni = Oturum("aln_admin");   // koruma sonrası taze oturum
        Assert.True(MaterialService.FiyatGorunur(yeni));
        Assert.Equal(55m, _mat.GetDetail(yeni, id).UnitPrice);
    }

    // ══════════════════ AL9–AL11 — ÇIKARIM KANALLARI ══════════════════

    /// <summary>
    /// 🔴 Değeri gizleyip filtreyi açık bırakmak gizlemek DEĞİLDİR: "fiyat = 100" filtresiyle
    /// gizli değer tek tek daraltılabilirdi. Filtre yok sayılmalı, sonuç kümesi DARALMAMALIDIR.
    /// </summary>
    [Fact]
    public void AL9_Gizli_Alanin_Filtresi_Yok_Sayilir()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        MalzemeOlustur(admin, "AL9-A", 100m);
        MalzemeOlustur(admin, "AL9-B", 900m);

        // Koruma YOKKEN filtre çalışıyor (ölçüm — varsayım değil).
        var acik = _mat.SearchGrid(admin, new MaterialGridFilter(UnitPrice: "100"), 1, 50);
        Assert.Equal(1, acik.TotalCount);

        FiyatiKoru();
        var s = Oturum("aln_personel");
        var kapali = _mat.SearchGrid(s, new MaterialGridFilter(UnitPrice: "100"), 1, 50);
        Assert.Equal(2, kapali.TotalCount);   // filtre düştü → hiçbir bilgi sızmadı
    }

    /// <summary>🔴 Fiyata göre sıralama da bir çıkarım kanalıdır (sıra = büyüklük bilgisi).</summary>
    [Fact]
    public void AL10_Gizli_Alana_Gore_Siralama_Duser()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        MalzemeOlustur(admin, "AL10-Z", 1m);      // koda göre SON, fiyata göre İLK
        MalzemeOlustur(admin, "AL10-A", 999m);    // koda göre İLK, fiyata göre SON

        var acik = _mat.SearchGrid(admin, new MaterialGridFilter(), 1, 50, MaterialListColumns.UnitPrice);
        Assert.Equal("AL10-Z", acik.Items[0].Code);   // sıralama gerçekten çalışıyor

        FiyatiKoru();
        var s = Oturum("aln_personel");
        var kapali = _mat.SearchGrid(s, new MaterialGridFilter(), 1, 50, MaterialListColumns.UnitPrice);
        Assert.Equal("AL10-A", kapali.Items[0].Code);   // varsayılan (kod) sıralamasına düştü
    }

    /// <summary>🔴 "Stok değeri" = stok × birim fiyat. Fiyat gizliyken bu toplam da fiyatın türevidir.</summary>
    [Fact]
    public void AL11_Fiyattan_Tureyen_Stok_Degeri_Ozeti_Gizlenir()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        MalzemeOlustur(admin, "AL11", 250m);

        Assert.True(_mat.SearchGridSummary(admin, new MaterialGridFilter()).StockValueVisible);

        FiyatiKoru();
        var s = Oturum("aln_personel");
        var ozet = _mat.SearchGridSummary(s, new MaterialGridFilter());
        Assert.False(ozet.StockValueVisible);
        Assert.Equal(0m, ozet.StockValue);
    }

    // ══════════════════ AL12 — DIŞA AKTARIM ══════════════════

    /// <summary>
    /// ⭐ Hücreyi boş bırakmak "gizleme" değildir; kolon BAŞLIĞIYLA birlikte düşmelidir
    /// (kullanıcı şartı §13). Aksi hâlde tabloda "Birim Fiyat: 0,00" yanlış bilgi olurdu.
    /// </summary>
    [Fact]
    public void AL12_Disa_Aktarimda_Kolon_Basligiyla_Birlikte_Duser()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        MalzemeOlustur(admin, "AL12", 12m);
        var rows = _mat.SearchGridAll(admin, new MaterialGridFilter());

        var acik = MaterialService.ToTableModel(rows, unitPriceVisible: true);
        Assert.Contains("Birim Fiyat", acik.Headers);
        Assert.Equal(acik.Headers.Count, acik.Rows[0].Count);

        var kapali = MaterialService.ToTableModel(rows, unitPriceVisible: false);
        Assert.DoesNotContain("Birim Fiyat", kapali.Headers);
        Assert.Equal(acik.Headers.Count - 1, kapali.Headers.Count);
        Assert.Equal(kapali.Headers.Count, kapali.Rows[0].Count);   // başlık ve hücre birlikte düştü

        // Kalan kolonların sırası bozulmadı (kod ilk, ad ikinci…).
        Assert.Equal("AL12", kapali.Rows[0][0]);
    }

    // ══════════════════ AL13 — TENANT ══════════════════

    /// <summary>Koruma FİRMA bazlıdır: A firmasının kararı B firmasının kullanıcısını etkilemez.</summary>
    [Fact]
    public void AL13_Koruma_Firma_Sinirini_Asmaz()
    {
        FiyatiKoru();   // yalnız Co firmasında

        var users = new UserService(_f);
        var bId = users.EnsureInitialAdmin(CoB, "aln_b", Pass, RoleKeys.Staff);
        _perms.SaveForUser(SuperAdmin(CoB), bId, new[] { Tam("materials") }, Array.Empty<string>());

        var rb = _auth.Login(CoB, "aln_b", Pass);
        Assert.True(rb.Success);
        var sb = rb.Session!;

        Assert.Empty(sb.ProtectedFields);
        Assert.True(MaterialService.FiyatGorunur(sb));   // B firması etkilenmedi

        var sa = Oturum("aln_personel");
        Assert.Single(sa.ProtectedFields);
    }

    // ══════════════════ AL14 — ROL ══════════════════

    /// <summary>Faz 3a birleşimi <c>module_key</c>'e bakmaz → <c>fld_</c> anahtarı ROL seviyesinde
    /// kendiliğinden çalışır. (RL11 aynı şeyi <c>rpt_</c> için kanıtlamıştı.)</summary>
    [Fact]
    public void AL14_Alan_Izni_Rol_Uzerinden_De_Calisir()
    {
        PersoneleMalzemeYetkisi();
        var admin = Oturum("aln_admin");
        var id = MalzemeOlustur(admin, "AL14", 64m);
        FiyatiKoru();

        var anahtar = FieldAccess.Key(FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice);
        _perms.SaveForRoleKey(SuperAdmin(), RoleKeys.Staff,
            new[] { new ModulePermission(anahtar, true, false, true, false) }, Array.Empty<string>());

        var s = Oturum("aln_personel");
        Assert.True(MaterialService.FiyatGorunur(s));
        Assert.Equal(64m, _mat.GetDetail(s, id).UnitPrice);
    }

    // ══════════════════ AL15–AL16 — YÖNETİM KAPILARI ══════════════════

    /// <summary>Katalogda olmayan alan korunamaz: serviste süzülmeyen bir alanı "korumalı" yapmak
    /// yöneticiye sahte güvence verirdi (fail-closed).</summary>
    [Fact]
    public void AL15_Katalog_Disi_Alan_Korunamaz()
    {
        Assert.Throws<ArgumentException>(() => _koruma.Set(SuperAdmin(), "materials", "uydurma_alan", true));
        Assert.Throws<ArgumentException>(() => _koruma.Set(SuperAdmin(), "uydurma_ekran", "unit_price", true));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));
    }

    [Fact]
    public void AL16_Koruma_Ayari_Yetki_Gerektirir()
    {
        PersoneleMalzemeYetkisi();
        var s = Oturum("aln_personel");
        Assert.Throws<ForbiddenException>(() =>
            _koruma.Set(s, FieldProtectionCatalog.Materials, FieldProtectionCatalog.UnitPrice, true));
        Assert.Throws<ForbiddenException>(() => _koruma.List(s));
        Assert.Equal(0L, Say("SELECT COUNT(*) FROM field_protections;"));
    }

    // ══════════════════ AL17–AL19 — ÖN MUHASEBE ══════════════════

    [Fact]
    public void AL17_Cari_Bakiyesi_Gizlenir()
    {
        var admin = Oturum("aln_admin");
        var parties = new PartyService(_f);
        var ledger = new PartyLedgerService(_f);

        var pid = parties.Create(admin, new NewParty("AL17", "AL17 Cari", PartyTypes.Customer));
        ledger.Add(admin, new NewLedgerEntry(pid, PartyDocTypes.Adjustment, 500m, IsDebit: true,
            OperationId: Guid.NewGuid().ToString("N")));

        Assert.Equal(500m, ledger.Balance(admin, pid).Debit);

        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("parties") }, Array.Empty<string>());
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance, true);

        var s = Oturum("aln_personel");
        var b = ledger.Balance(s, pid);
        Assert.Equal(0m, b.Debit);
        Assert.Equal(0m, b.Credit);
        Assert.Equal(1, b.EntryCount);   // hareketin VARLIĞI tutar değildir → gizlenmez

        var satir = parties.List(s).Items.Single(x => x.Party.Id == pid);
        Assert.Equal(0m, satir.Debit);
        Assert.Equal(0m, satir.Balance);

        // Ekstrede yürüyen bakiye de kapalı (iki satırın farkı tutarı verirdi).
        Assert.All(ledger.Statement(s, pid), x =>
        {
            Assert.Equal(0m, x.Entry.Amount);
            Assert.Equal(0m, x.RunningBalance);
        });

        // 🔴 Veri yerinde.
        Assert.Equal(500m, ledger.Balance(Oturum("aln_admin"), pid).Debit);
    }

    [Fact]
    public void AL18_Fatura_Tutari_Gizlenir_Detay_Fail_Closed()
    {
        var admin = Oturum("aln_admin");
        var parties = new PartyService(_f);
        var ledger = new PartyLedgerService(_f);
        var invoices = new InvoiceService(_f, new StockService(_f), ledger);
        var reads = new InvoiceQueryService(_f);

        var pid = parties.Create(admin, new NewParty("AL18", "AL18 Cari", PartyTypes.Customer));
        // AffectsStock=false: hizmet faturası — bu test STOK yolunu değil ALAN yetkisini ölçer.
        var iid = invoices.Create(admin, new NewInvoice(InvoiceDirections.Sales, pid,
            new[] { new NewInvoiceLine(null, "Hizmet", null, 1m, 1000m) },
            Guid.NewGuid().ToString("N"), AffectsStock: false)).Id;

        Assert.Equal(1000m, reads.List(admin).Items.Single(x => x.Id == iid).GrandTotal);

        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("invoices") }, Array.Empty<string>());
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Invoices, FieldProtectionCatalog.GrandTotal, true);

        var s = Oturum("aln_personel");
        var satir = reads.List(s).Items.Single(x => x.Id == iid);
        Assert.Equal(0m, satir.GrandTotal);
        Assert.False(string.IsNullOrWhiteSpace(satir.InvoiceNo));   // faturanın VARLIĞI görünür

        // Detay baştan sona tutardır → 403 (sıfırlanmış "detay" yanlış bilgi olurdu).
        Assert.Throws<ForbiddenException>(() => { reads.Get(s, iid); });
    }

    [Fact]
    public void AL19_Kasa_Banka_Tutari_Gizlenir()
    {
        var admin = Oturum("aln_admin");
        var finance = new FinanceService(_f, new PartyLedgerService(_f));
        var reads = new FinanceQueryService(_f);

        var aid = finance.CreateAccount(admin, new NewFinanceAccount("AL19", "AL19 Kasa", FinanceAccountKinds.Cash));
        // Tahsilat cari GEREKTİRİR (FinanceService.Validate) — iş kuralı, test buna uyar.
        var pid = new PartyService(_f).Create(admin, new NewParty("AL19P", "AL19 Cari", PartyTypes.Customer));
        finance.Add(admin, new NewFinanceEntry(aid, FinanceTxnTypes.Receipt, 750m,
            Guid.NewGuid().ToString("N"), PartyId: pid));

        Assert.Equal(750m, reads.Balance(admin, aid));

        _perms.SaveForUser(SuperAdmin(), _personelId, new[] { Tam("finance") }, Array.Empty<string>());
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Finance, FieldProtectionCatalog.Amount, true);
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Finance, FieldProtectionCatalog.Balance, true);

        var s = Oturum("aln_personel");
        Assert.Equal(0m, reads.Balance(s, aid));
        Assert.All(reads.Accounts(s), x => Assert.Equal(0m, x.Balance));
        Assert.All(reads.Statement(s, aid), x =>
        {
            Assert.Equal(0m, x.Txn.Amount);
            Assert.Equal(0m, x.RunningBalance);
        });
        Assert.All(reads.Transactions(s).Items, x => Assert.Equal(0m, x.Amount));

        Assert.Equal(750m, reads.Balance(Oturum("aln_admin"), aid));   // veri yerinde
    }

    // ══════════════════ AL20 — YENİ KAYIT ══════════════════

    [Fact]
    public void AL20_Yeni_Kayitta_Gizli_Alan_Yazilmaz()
    {
        PersoneleMalzemeYetkisi();
        FiyatiKoru();

        var s = Oturum("aln_personel");
        var id = _mat.Create(s, new NewMaterial("AL20", "AL20 adı", UnitPrice: 5000m));
        Assert.Equal(0m, HamFiyat(id));   // gönderilen değer YOK SAYILDI
    }

    // ══════════════════ AL21 — ÖNBELLEK ══════════════════

    /// <summary>Koruma açıldığı anda etkili olmalı; bayat yetki fotoğrafı kalmamalı.</summary>
    [Fact]
    public void AL21_Koruma_Degisince_Yetki_Fotografi_Duser()
    {
        PersoneleMalzemeYetkisi();
        var s1 = _auth.CreateSessionForUser(Co, _personelId)!;
        Assert.True(MaterialService.FiyatGorunur(s1));
        Assert.True(_cache.Count > 0);

        FiyatiKoru();
        Assert.Equal(0, _cache.Count);   // InvalidateAll çalıştı

        var s2 = _auth.CreateSessionForUser(Co, _personelId)!;
        Assert.False(MaterialService.FiyatGorunur(s2));
    }

    // ══════════════════ AL22 — PERFORMANS ══════════════════

    /// <summary>
    /// ⭐ Kullanıcı şartı §17/§37: 10.000 kayıtta karar SORGU başına verilir, satır başına DEĞİL.
    /// Test bunu iki şekilde ölçer: (1) korumalı ve korumasız koşuların süresi aynı büyüklük
    /// sınıfında kalır, (2) toplam süre kabul eşiğinin altındadır.
    /// </summary>
    [Fact]
    public void AL22_On_Bin_Kayitta_Karar_Sorgu_Basinadir()
    {
        var admin = Oturum("aln_admin");
        const int adet = 10_000;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Toplu ekleme: servis üzerinden 10.000 kayıt açmak testin kendisini yavaşlatırdı;
        // ölçülen şey OKUMA yolu olduğu için veri doğrudan yazılır (şema aynı).
        using (var conn = _f.Create())
        using (var tx = conn.BeginTransaction())
        {
            for (int i = 0; i < adet; i++)
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
INSERT INTO materials(id, company_id, code, name, min_stock, unit_price, currency_code,
    created_at, updated_at, version, is_deleted)
VALUES(@id,@c,@code,@name,'0',@p,'TRY',@now,@now,1,0);";
                cmd.AddWithValue("@id", "perf" + i);
                cmd.AddWithValue("@c", Co);
                cmd.AddWithValue("@code", "PERF-" + i.ToString("D5"));
                cmd.AddWithValue("@name", "Perf " + i);
                cmd.AddWithValue("@p", Money.Serialize(1m + i % 100));
                cmd.AddWithValue("@now", now);
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var acik = _mat.SearchGridAll(admin, new MaterialGridFilter());
        var acikMs = sw.ElapsedMilliseconds;
        Assert.True(acik.Count >= adet, $"Beklenen ≥{adet}, gelen {acik.Count}");

        PersoneleMalzemeYetkisi();
        FiyatiKoru();
        var s = Oturum("aln_personel");

        sw.Restart();
        var kapali = _mat.SearchGridAll(s, new MaterialGridFilter());
        var kapaliMs = sw.ElapsedMilliseconds;

        Assert.Equal(acik.Count, kapali.Count);
        Assert.All(kapali, r => Assert.Equal(0m, r.UnitPrice));

        // Satır başına ek sorgu olsaydı 10.000 gidiş-dönüş eklenir, süre kat kat artardı.
        // Eşik gevşek tutuldu (makine hızı değişir); yakalamak istediği şey BÜYÜKLÜK SINIFI farkıdır.
        Assert.True(kapaliMs < Math.Max(3 * acikMs + 1000, 4000),
            $"Alan süzgeci ölçülebilir maliyet getirdi: korumasız {acikMs} ms, korumalı {kapaliMs} ms.");
    }

    // ══════════════════ AL23 — RAPOR KAÇAĞI ══════════════════

    /// <summary>
    /// 🔴 Ön muhasebe raporları tutarları SERVİSLERDEN değil doğrudan SQL'den okur. Bu, alanı
    /// ekranda gizleyip raporda açık bırakan klasik kaçaktır: "Cari Bakiye Raporu" aynı sayıyı
    /// verirdi. Rapor tamamen tutardan oluştuğu için süzülmez, <b>fail-closed kapatılır</b>.
    ///
    /// Test, koruma AÇILMADAN raporun gerçekten çalıştığını da ölçer — yoksa "hep 403 dönüyor"
    /// diye yanlış bir yeşil elde edilirdi.
    /// </summary>
    [Fact]
    public void AL23_On_Muhasebe_Raporlari_Alan_Kapisindan_Gecer()
    {
        var admin = Oturum("aln_admin");
        var raporlar = new DepoWise.Infrastructure.Reporting.ReportService(_f);

        // Koruma YOKKEN rapor çalışıyor (ölçüm — varsayım değil).
        var once = raporlar.Run(admin, "acc-balances", new DepoWise.Application.Reports.ReportRequest(Executed: true));
        Assert.NotNull(once);

        _perms.SaveForUser(SuperAdmin(), _personelId,
            new[] { Tam("parties"), Tam("invoices"), Tam("finance"), Tam("reports") }, Array.Empty<string>());
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Parties, FieldProtectionCatalog.Balance, true);
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Invoices, FieldProtectionCatalog.GrandTotal, true);
        _koruma.Set(SuperAdmin(), FieldProtectionCatalog.Finance, FieldProtectionCatalog.Amount, true);

        var s = Oturum("aln_personel");
        foreach (var anahtar in new[] { "acc-statement", "acc-balances", "acc-invoices",
                                        "acc-open-invoices", "acc-payments", "acc-cash" })
        {
            var ex = Assert.Throws<ForbiddenException>(() =>
            {
                raporlar.Run(s, anahtar, new DepoWise.Application.Reports.ReportRequest(Executed: true));
            });
            Assert.Contains("yetkiniz yok", ex.Message);
        }

        // Kapsam dışı rapor ETKİLENMEZ — kapı yalnız korunan alanı taşıyan raporları kapatır.
        Assert.NotNull(raporlar.Run(Oturum("aln_admin"), "acc-balances", new DepoWise.Application.Reports.ReportRequest(Executed: true)));
    }
}
