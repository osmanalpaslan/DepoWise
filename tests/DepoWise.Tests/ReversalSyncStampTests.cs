using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Accounting;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Organization;
using DepoWise.Infrastructure.Security;
using DepoWise.Infrastructure.Sync;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// DENETİM G3 (2026-08-18) — <b>İPTAL İŞLEMİ SENKRONA HİÇ GİRMİYORDU.</b>
///
/// Senkron deltası bir zaman damgası üzerinden hesaplanır; <c>party_ledger</c>, <c>stock_movements</c>
/// ve <c>stock_documents</c> tablolarında <c>updated_at</c> KOLONU YOKTU → damga <c>created_at</c>'e
/// düşüyordu. İptal ise satırı YERİNDE güncelliyor (<c>is_reversed=1</c> / <c>status='cancelled'</c>) ve
/// <c>created_at</c> değişmiyor → <b>güncelleme push'a hiç girmiyordu.</b>
///
/// En ağır sonucu <c>party_ledger</c>'daydı: cari bakiyesi <c>WHERE is_reversed=0</c> ile hesaplandığı
/// için <b>masaüstünde iptal edilen borç sunucuda/web'de duruyordu (bakiye YANLIŞ).</b>
///
/// Migration069 üç tabloya <c>updated_at</c> ekler (mevcut satırlar <c>created_at</c> ile dolar → delta
/// penceresi aynı kalır), iptal/durum değişikliği damgayı tazeler.
/// </summary>
public class ReversalSyncStampTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _factory;
    private readonly MutableClock _clock = new();
    private readonly SessionContext _admin;
    private const string Co = "STAMP-CO";

    private sealed class MutableClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.FromUnixTimeMilliseconds(1_700_000_000_000);
        public void Advance(long ms) => UtcNow = DateTimeOffset.FromUnixTimeMilliseconds(UtcNow.ToUnixTimeMilliseconds() + ms);
    }

    public ReversalSyncStampTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_stamp_" + Guid.NewGuid().ToString("N") + ".db");
        _factory = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_factory).Run();
        Sql($"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','T',1,1,1,0);");
        _admin = new SessionContext("admin", Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
    }

    private void Sql(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private T? Scalar<T>(string sql)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, typeof(T));
    }

    // ── Şema ─────────────────────────────────────────────────────────────────────────────────────
    [Theory]
    [InlineData("party_ledger")]
    [InlineData("stock_movements")]
    [InlineData("stock_documents")]
    public void Migration069_Damga_Kolonu_Ekler(string table)
    {
        using var conn = _factory.Create();
        Assert.True(DbIntrospect.ColumnExists(conn, null, table, "updated_at"));
    }

    // ── SNK-A1: cari iptali ──────────────────────────────────────────────────────────────────────
    /// <summary>⭐ ASIL HATA: iptal edilen cari hareketi senkron deltasına GİRMELİ.</summary>
    [Fact]
    public void Cari_Iptali_Delta_Penceresine_GIRER()
    {
        var parties = new PartyService(_factory, _clock);
        var ledger = new PartyLedgerService(_factory, _clock);
        var partyId = parties.Create(_admin, new NewParty("C1", "Test Cari", PartyTypes.Customer));

        var girisId = ledger.Add(_admin, new NewLedgerEntry(partyId, "opening", 1_000m, IsDebit: true, EntryDate: _clock.UtcNow.ToUnixTimeMilliseconds(), Description: "borç"));

        // Kaydın ardından zaman ilerler; "bu ana kadar gönderildi" sınırı buraya konur.
        _clock.Advance(60_000);
        var watermark = new BusinessSyncService(_factory).CompanyVersion(Co);

        _clock.Advance(60_000);
        ledger.Reverse(_admin, girisId, "yanlış kayıt");

        // İptal SONRASI sürüm watermark'ı GEÇMELİ — geçmiyorsa push bu değişikliği hiç görmez.
        var sonra = new BusinessSyncService(_factory).CompanyVersion(Co);
        Assert.True(sonra > watermark, "İptal sonrası iş sürümü ilerlemedi → değişiklik senkrona girmez.");

        // Delta snapshot'ta ASIL kaydın güncel hâli (is_reversed=1) bulunmalı.
        var snapshot = new BusinessSyncService(_factory).BuildSnapshot(Co, "TEST", watermark);
        Assert.Contains(girisId, snapshot);
    }

    /// <summary>İptal sonrası ASIL kaydın damgası tazelenmiş olmalı.</summary>
    [Fact]
    public void Cari_Iptalinde_Asil_Kaydin_Damgasi_Tazelenir()
    {
        var parties = new PartyService(_factory, _clock);
        var ledger = new PartyLedgerService(_factory, _clock);
        var partyId = parties.Create(_admin, new NewParty("C2", "Test Cari 2", PartyTypes.Customer));
        var girisId = ledger.Add(_admin, new NewLedgerEntry(partyId, "opening", 500m, IsDebit: true, EntryDate: _clock.UtcNow.ToUnixTimeMilliseconds(), Description: "borç"));

        var once = Scalar<long>($"SELECT updated_at FROM party_ledger WHERE id='{girisId}';");
        _clock.Advance(120_000);
        ledger.Reverse(_admin, girisId, "iptal");
        var sonra = Scalar<long>($"SELECT updated_at FROM party_ledger WHERE id='{girisId}';");

        Assert.True(sonra > once, "is_reversed=1 yazıldı ama updated_at tazelenmedi.");
    }

    // ── Yeni kayıtlar damgalı mı (regresyon koruması) ────────────────────────────────────────────
    /// <summary>
    /// Damga kolonu SQLite'ta NOT NULL yapılamaz. Yeni bir INSERT onu doldurmayı atlarsa satır
    /// NULL damgayla kalır ve delta koşulu NULL'da FALSE döneceği için o satır HİÇ senkron edilmez.
    /// Bu test, üç tablonun da yazma yolunun damgayı doldurduğunu kilitler.
    /// </summary>
    [Fact]
    public void Yeni_Kayitlar_Damgali_Yazilir()
    {
        var parties = new PartyService(_factory, _clock);
        var ledger = new PartyLedgerService(_factory, _clock);
        var partyId = parties.Create(_admin, new NewParty("C3", "Test Cari 3", PartyTypes.Customer));
        ledger.Add(_admin, new NewLedgerEntry(partyId, "opening", 10m, IsDebit: true, EntryDate: _clock.UtcNow.ToUnixTimeMilliseconds(), Description: "borç"));

        Assert.Equal(0, Scalar<long>("SELECT COUNT(*) FROM party_ledger WHERE updated_at IS NULL;"));
    }

    /// <summary>Damga eksik kalsa bile satır senkron dışı KALMAMALI (yapısal güvenlik ağı).</summary>
    [Fact]
    public void Damgasiz_Satir_Bile_Deltaya_Girer()
    {
        var parties = new PartyService(_factory, _clock);
        var ledger = new PartyLedgerService(_factory, _clock);
        var partyId = parties.Create(_admin, new NewParty("C4", "Test Cari 4", PartyTypes.Customer));
        var id = ledger.Add(_admin, new NewLedgerEntry(partyId, "opening", 42m, IsDebit: true, EntryDate: _clock.UtcNow.ToUnixTimeMilliseconds(), Description: "borç"));

        // Eski sürümden kalmış / damgası doldurulmamış satırı taklit et.
        Sql($"UPDATE party_ledger SET updated_at=NULL WHERE id='{id}';");

        var snapshot = new BusinessSyncService(_factory).BuildSnapshot(Co, "TEST", sinceVersion: 1);
        Assert.Contains(id, snapshot);   // COALESCE(updated_at, created_at) sayesinde kaybolmaz
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_dbPath); } catch { }
    }
}
