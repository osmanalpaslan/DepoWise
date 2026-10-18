using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Vehicles;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ M-30 (AR-GE 2026-10-18, 05 F-076) — DÖNEM KİLİDİ (ay kapanışı) ═══
///  DK1 — Kural: kilit günü DAHİL öncesi reddedilir, ertesi gün serbest; kilit yoksa hiçbir şey değişmez
///  DK2 — Ayarlama: yalnız yönetici; bugün/ileri tarih reddedilir; gün başına yuvarlanır; işlem geçmişine yazılır; kaldırılabilir
///  DK3 — Oturum: giriş ve sunucu oturumu kilidi taşır; ayar değişince önbellek beklenmeden yeni değer gelir
///  DK4 — Masaüstü giriş paketi kilidi taşır ve yerel firma satırına yazar (çevrimdışı da uygulanır)
///  DK5 — Stok: kapalı döneme geri tarihli giriş ve kapalı dönem belgesinin iptali reddedilir; açık dönem serbest
///  DK6 — Fatura ve finans: kapalı döneme kayıt ve kapalı dönem kaydının iptali reddedilir; tekrar gönderim zararsız
///  DK7 — Yakıt ve bakım: kapalı döneme kayıt ve iptal reddedilir
///  DK8 — Eski şema (kolon yok): kilit yok sayılır, ayar anlaşılır hata verir
///  DK9 — İki platform + API (kaynak kilidi)
/// </summary>
public class DonemKilidiTests : IDisposable
{
    private const string Co = "DKL";
    private const long G = DonemKilidi.GunMs;
    private readonly string _yol = Path.Combine(Path.GetTempPath(), "depowise_dk_" + Guid.NewGuid().ToString("N") + ".db");
    private readonly List<string> _ekYollar = new();
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _s;
    private readonly string _uid, _sube, _mat;
    private readonly StockService _stock;
    private readonly long _bugun;

    public DonemKilidiTests()
    {
        _f = new SqliteConnectionFactory(_yol);
        new MigrationRunner(_f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);");
        _uid = new UserService(_f).EnsureInitialAdmin(Co, "dk", "Donem!2026", RoleKeys.CompanyAdmin);
        _s = new SessionContext(_uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new BranchService(_f).Create(_s, new NewBranch("DK Depo"));
        _mat = new MaterialService(_f).Create(_s, new NewMaterial("DK-M", "Filtre"));
        _stock = new StockService(_f);
        var simdi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _bugun = simdi - simdi % G;
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var y in _ekYollar.Append(_yol))
            foreach (var p in new[] { y, y + "-wal", y + "-shm" }) try { File.Delete(p); } catch { }
    }

    private void Exec(string sql, SqliteConnectionFactory? f = null)
    {
        using var c = (f ?? _f).Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
    }

    private object? Tek(string sql, SqliteConnectionFactory? f = null)
    {
        using var c = (f ?? _f).Create(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }

    private static string Op() => "op-" + Guid.NewGuid().ToString("N");

    /// <summary>Kilidi <paramref name="gunOnce"/> gün önceye kurar ve oturuma da yansıtır (sunucu oturumu gibi).</summary>
    private void Kilitle(int gunOnce)
    {
        new DonemKilidiService(_f).Ayarla(_s, _bugun - gunOnce * G);
        _s.DonemKilidi = new DonemKilidiService(_f).Oku(Co);
    }

    // ══════════════════ DK1 — KURAL ══════════════════

    [Fact]
    public void DK1_Kilit_Gunu_Dahil_Oncesi_Reddedilir()
    {
        var s = new SessionContext("u", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        var kilit = _bugun - 10 * G;
        DonemKilidi.Dogrula(s, kilit - 5 * G);           // kilit yok → serbest
        s.DonemKilidi = kilit;
        var ex = Assert.Throws<ArgumentException>(() => DonemKilidi.Dogrula(s, kilit + G - 1, "fatura"));   // kilit gününün son anı
        Assert.Contains(DonemKilidi.Tarih(kilit), ex.Message);
        Assert.Contains("fatura", ex.Message);
        Assert.Throws<ArgumentException>(() => DonemKilidi.Dogrula(s, kilit - 30 * G));
        DonemKilidi.Dogrula(s, kilit + G);               // ertesi gün başı → serbest
        DonemKilidi.Dogrula(s, null);                    // "şimdi" → serbest
        // Geri tarih kapısı da kilide takılır (yetkili kullanıcı bile kapalı döneme kayıt açamaz)
        Assert.Throws<ArgumentException>(() => DateEntryPolicy.Uygula(s, kilit - G));
        Assert.Equal(kilit + 2 * G, DateEntryPolicy.Uygula(s, kilit + 2 * G));
    }

    // ══════════════════ DK2 — AYARLAMA ══════════════════

    [Fact]
    public void DK2_Ayarlama_Yonetici_Yuvarlama_Denetim_Kaldirma()
    {
        var svc = new DonemKilidiService(_f);
        var personel = new SessionContext(_uid, Co, new[] { RoleKeys.Staff }, PermissionSet.Empty);
        Assert.Throws<ForbiddenException>(() => svc.Ayarla(personel, _bugun - 5 * G));
        Assert.Throws<ArgumentException>(() => svc.Ayarla(_s, _bugun));             // bugün kilitlenemez
        Assert.Throws<ArgumentException>(() => svc.Ayarla(_s, _bugun + 3 * G));     // ileri tarih kilitlenemez
        Assert.Null(svc.Oku(Co));

        svc.Ayarla(_s, _bugun - 5 * G + 13 * 3_600_000L);    // gün ortası → gün başına yuvarlanır
        Assert.Equal(_bugun - 5 * G, svc.Oku(Co));
        Assert.Equal(_bugun - 5 * G, Convert.ToInt64(Tek($"SELECT period_lock_date FROM companies WHERE id='{Co}';")));
        var iz = Tek($"SELECT after_json FROM audit_logs WHERE company_id='{Co}' AND entity_type='company' ORDER BY rowid DESC LIMIT 1;") as string;
        Assert.Contains(DonemKilidi.Tarih(_bugun - 5 * G), iz);

        svc.Ayarla(_s, null);
        Assert.Null(svc.Oku(Co));
        Assert.True(Tek($"SELECT period_lock_date FROM companies WHERE id='{Co}';") is DBNull);
    }

    // ══════════════════ DK3 — OTURUM ══════════════════

    [Fact]
    public void DK3_Oturum_Kilidi_Tasir_Ve_Aninda_Guncellenir()
    {
        var auth = new AuthService(_f);
        Assert.Null(auth.CreateSessionForUser(Co, _uid)!.DonemKilidi);   // önbelleği doldur (kilit yok)
        new DonemKilidiService(_f).Ayarla(_s, _bugun - 7 * G);
        Assert.Equal(_bugun - 7 * G, auth.CreateSessionForUser(Co, _uid)!.DonemKilidi);   // 30 sn beklemeden
        var giris = auth.Login(Co, "dk", "Donem!2026");
        Assert.True(giris.Success, giris.Error);
        Assert.Equal(_bugun - 7 * G, giris.Session!.DonemKilidi);
    }

    // ══════════════════ DK4 — MASAÜSTÜ GİRİŞ PAKETİ ══════════════════

    [Fact]
    public void DK4_Giris_Paketi_Kilidi_Yerel_Firmaya_Yazar()
    {
        new DonemKilidiService(_f).Ayarla(_s, _bugun - 4 * G);
        var paket = new AuthService(_f).ExportForSync(Co, "dk", "Donem!2026");
        Assert.NotNull(paket);
        Assert.Equal(_bugun - 4 * G, paket!.DonemKilidi);

        var yerelYol = Path.Combine(Path.GetTempPath(), "depowise_dk_yerel_" + Guid.NewGuid().ToString("N") + ".db");
        _ekYollar.Add(yerelYol);
        var yerel = new SqliteConnectionFactory(yerelYol);
        new MigrationRunner(yerel).Run();
        var yerelAuth = new AuthService(yerel);
        Assert.Null(new DonemKilidiService(yerel).Oku(Co));          // önbelleği "kilit yok" ile doldur
        yerelAuth.ImportRemoteUser(paket);
        Assert.Equal(_bugun - 4 * G, Convert.ToInt64(Tek($"SELECT period_lock_date FROM companies WHERE id='{Co}';", yerel)));
        Assert.Equal(_bugun - 4 * G, new DonemKilidiService(yerel).Oku(Co));
        Assert.Equal(_bugun - 4 * G, yerelAuth.Login(Co, "dk", "Donem!2026").Session!.DonemKilidi);   // çevrimdışı giriş

        // Sunucuda kilit kaldırılınca bir sonraki paket yereldeki kilidi de kaldırır
        new DonemKilidiService(_f).Ayarla(_s, null);
        yerelAuth.ImportRemoteUser(new AuthService(_f).ExportForSync(Co, "dk", "Donem!2026")!);
        Assert.True(Tek($"SELECT period_lock_date FROM companies WHERE id='{Co}';", yerel) is DBNull);
    }

    // ══════════════════ DK5 — STOK ══════════════════

    [Fact]
    public void DK5_Stok_Kapali_Doneme_Kayit_Ve_Iptal_Reddedilir()
    {
        var eski = _stock.ReceiveIn(_s, new[] { new StockLine(_mat, 10m) }, Op(), _sube, docDate: _bugun - 20 * G);
        Kilitle(10);
        var bakiye = Tek($"SELECT group_concat(quantity) FROM stock_balances WHERE material_id='{_mat}';");

        var ex = Assert.Throws<ArgumentException>(() =>
            _stock.ReceiveIn(_s, new[] { new StockLine(_mat, 5m) }, Op(), _sube, docDate: _bugun - 12 * G));
        Assert.Contains("dönem kapatıldı", ex.Message);
        Assert.Throws<ArgumentException>(() => _stock.ReverseDocument(_s, eski.DocumentId, "yanlış"));
        Assert.Equal("active", Tek($"SELECT status FROM stock_documents WHERE id='{eski.DocumentId}';"));
        Assert.Equal(bakiye, Tek($"SELECT group_concat(quantity) FROM stock_balances WHERE material_id='{_mat}';"));   // hiçbir şey değişmedi

        // Açık dönem: kilit sonrası gün ve bugün serbest; o belgenin iptali de serbest
        var yeni = _stock.ReceiveIn(_s, new[] { new StockLine(_mat, 5m) }, Op(), _sube, docDate: _bugun - 9 * G);
        _stock.ReceiveIn(_s, new[] { new StockLine(_mat, 1m) }, Op(), _sube);
        _stock.ReverseDocument(_s, yeni.DocumentId, "yanlış");
        Assert.Equal("cancelled", Tek($"SELECT status FROM stock_documents WHERE id='{yeni.DocumentId}';"));
    }

    // ══════════════════ DK6 — FATURA VE FİNANS ══════════════════

    [Fact]
    public void DK6_Fatura_Ve_Finans_Kapali_Donem()
    {
        var ledger = new PartyLedgerService(_f);
        var faturalar = new InvoiceService(_f, _stock, ledger);
        var finans = new FinanceService(_f, ledger);
        var cari = new PartyService(_f).Create(_s, new NewParty("C-1", "Örnek Ltd.", PartyTypes.Both));
        var kasa = finans.CreateAccount(_s, new NewFinanceAccount("K-1", "Kasa", FinanceAccountKinds.Cash, BranchId: _sube));
        _stock.ReceiveIn(_s, new[] { new StockLine(_mat, 50m) }, Op(), _sube, docDate: _bugun - 30 * G);
        NewInvoice Fatura(string op, long tarih) => new(InvoiceDirections.Sales, cari,
            new[] { new NewInvoiceLine(_mat, null, null, 1m, 100m) }, op, BranchId: _sube, InvoiceDate: tarih);

        var eskiOp = Op();
        var eskiFatura = faturalar.Create(_s, Fatura(eskiOp, _bugun - 20 * G)).Id;
        var eskiTahsilat = finans.Add(_s, new NewFinanceEntry(kasa, FinanceTxnTypes.Receipt, 40m, Op(),
            PartyId: cari, BranchId: _sube, TxnDate: _bugun - 20 * G)).TransactionId;
        Kilitle(10);

        Assert.Throws<ArgumentException>(() => faturalar.Create(_s, Fatura(Op(), _bugun - 15 * G)));
        Assert.Equal(eskiFatura, faturalar.Create(_s, Fatura(eskiOp, _bugun - 20 * G)).Id);   // tekrar gönderim: mevcut döner, hata yok
        Assert.Throws<ArgumentException>(() => faturalar.Cancel(_s, eskiFatura, "yanlış"));
        Assert.Equal(InvoiceStatuses.Active, Tek($"SELECT status FROM invoices WHERE id='{eskiFatura}';"));
        Assert.Throws<ArgumentException>(() => finans.Add(_s, new NewFinanceEntry(kasa, FinanceTxnTypes.Receipt, 10m, Op(),
            PartyId: cari, BranchId: _sube, TxnDate: _bugun - 11 * G)));
        Assert.Throws<ArgumentException>(() => finans.Reverse(_s, eskiTahsilat, "yanlış"));
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT is_reversed FROM finance_transactions WHERE id='{eskiTahsilat}';")));

        // Açık dönem serbest
        var yeni = faturalar.Create(_s, Fatura(Op(), _bugun - 5 * G)).Id;
        faturalar.Cancel(_s, yeni, "yanlış");
        var yeniTahsilat = finans.Add(_s, new NewFinanceEntry(kasa, FinanceTxnTypes.Receipt, 10m, Op(),
            PartyId: cari, BranchId: _sube)).TransactionId;
        finans.Reverse(_s, yeniTahsilat, "yanlış");
    }

    // ══════════════════ DK7 — YAKIT VE BAKIM ══════════════════

    [Fact]
    public void DK7_Yakit_Ve_Bakim_Kapali_Donem()
    {
        var yakit = new FuelService(_f);
        var bakim = new MaintenanceService(_f);
        var arac = new VehicleService(_f).Create(_s, new NewVehicle("DK-1", "06 DK 1", CurrentMeter: 1_000m));
        var tanim = new MaintenanceDefinitionService(_f).Create(_s, new NewMaintenanceDefinition("Yağ", 10_000m, "km"));
        var depo = yakit.AddDepotEntry(_s, new NewDepotEntry(1_000m, 40m, "TRY", EntryDate: _bugun - 20 * G), Op());
        var dagitim = yakit.Distribute(_s, new NewDistribution(arac, 50m, 1_100m, DistributionDate: _bugun - 20 * G), Op());
        var eskiBakim = bakim.Save(_s, new NewMaintenance(arac, tanim, PerformedKm: 1_200m, PerformedDate: _bugun - 20 * G), Op());
        Kilitle(10);

        Assert.Throws<ArgumentException>(() => yakit.AddDepotEntry(_s, new NewDepotEntry(10m, 40m, "TRY", EntryDate: _bugun - 15 * G), Op()));
        Assert.Throws<ArgumentException>(() => yakit.CancelDepotEntry(_s, depo, "yanlış"));
        Assert.Throws<ArgumentException>(() => yakit.CancelDistribution(_s, dagitim, "yanlış"));
        Assert.Throws<ArgumentException>(() => yakit.UpdateDistribution(_s, dagitim,
            new NewDistribution(arac, 40m, 1_100m), Op(), "düzeltme"));
        Assert.Equal(0L, Convert.ToInt64(Tek($"SELECT is_deleted FROM fuel_distributions WHERE id='{dagitim}';")));
        Assert.Throws<ArgumentException>(() => bakim.Save(_s, new NewMaintenance(arac, tanim, PerformedKm: 1_300m,
            PerformedDate: _bugun - 12 * G), Op()));
        Assert.Throws<ArgumentException>(() => bakim.Cancel(_s, eskiBakim, "yanlış"));

        // Açık dönem serbest
        var yeniBakim = bakim.Save(_s, new NewMaintenance(arac, tanim, PerformedKm: 1_300m, PerformedDate: _bugun - 2 * G), Op());
        bakim.Cancel(_s, yeniBakim, "yanlış");
        var yeniDagitim = yakit.Distribute(_s, new NewDistribution(arac, 10m, 1_400m), Op());
        yakit.CancelDistribution(_s, yeniDagitim, "yanlış");
    }

    // ══════════════════ DK8 — ESKİ ŞEMA ══════════════════

    [Fact]
    public void DK8_Eski_Sema_Kilit_Yok_Sayilir()
    {
        var yol = Path.Combine(Path.GetTempPath(), "depowise_dk_eski_" + Guid.NewGuid().ToString("N") + ".db");
        _ekYollar.Add(yol);
        var f = new SqliteConnectionFactory(yol);
        new MigrationRunner(f).Run();
        Exec($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);", f);
        Exec("ALTER TABLE companies DROP COLUMN period_lock_date;", f);
        var svc = new DonemKilidiService(f);
        Assert.Null(svc.Oku(Co));
        var ex = Assert.Throws<InvalidOperationException>(() => svc.Ayarla(_s, _bugun - 3 * G));
        Assert.Contains("desteklemiyor", ex.Message);
        using (var c = f.Create()) new Migration106_PeriodLock().Up(c, null!);   // tekrar çalışabilir (kolon yeniden eklenir)
        Assert.Null(svc.Oku(Co));
    }

    // ══════════════════ DK9 — KAYNAK KİLİDİ ══════════════════

    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));

    [Fact]
    public void DK9_Iki_Platform_Ve_Api()
    {
        var api = Oku("src/DepoWise.Api/Program.cs");
        Assert.Contains("app.MapGet(\"/api/company/period-lock\"", api);
        Assert.Contains("app.MapPut(\"/api/company/period-lock\"", api);
        Assert.Contains("svc.DonemKilidi.Ayarla(s, d.Tarih)", api);

        var mvm = Oku("src/DepoWise.Desktop/ViewModels/FieldSettingsViewModel.cs");
        Assert.Contains("DonemKilidiIstemci.AyarlaAsync(_session, ms)", mvm);
        Assert.Contains("DonemKilidiIstemci.AyarlaAsync(_session, null)", mvm);
        Assert.Contains("DonemKilidiKaydetCommand", Oku("src/DepoWise.Desktop/Views/FieldSettingsView.axaml"));
        Assert.Contains("/api/company/period-lock", Oku("src/DepoWise.Desktop/DonemKilidiIstemci.cs"));

        var web = Oku("src/DepoWise.Web/Components/Pages/FieldSettings.razor");
        Assert.Contains("Api.GetObjectAsync(\"/api/company/period-lock\")", web);
        Assert.Contains("Api.PutAsync(\"/api/company/period-lock\", new { tarih = ms })", web);
        Assert.Contains("Api.PutAsync(\"/api/company/period-lock\", new { tarih = (long?)null })", web);
    }
}
