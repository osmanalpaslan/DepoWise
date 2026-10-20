using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Vehicles;

/// <summary>⭐ M-39: kiralık makinenin dönem maliyeti (biz kiraladık) ya da geliri (kiraya verdik).</summary>
public sealed record KiraDonemSatiri(string VehicleId, string AracKodu, string? CariAdi, string Yon, string Birim, decimal Fiyat,
    decimal Miktar, decimal Tutar, long? Bitis)
{
    public bool GelirMi => Yon == KiraService.Disari;
    public string YonMetni => GelirMi ? "Kiraya verildi (gelir)" : "Kiralandı (maliyet)";
    public string Ozet => $"{AracKodu} · {CariAdi ?? "—"} · {YonMetni} · {Miktar:0.##} {RentalPriceUnits.Label(Birim)} × {Fiyat:N2} = {Tutar:N2}";
}

/// <summary>⭐ M-39: bitişi yaklaşan ya da geçmiş (iade edilmemiş) kiralama.</summary>
public sealed record KiraBitisUyarisi(string VehicleId, string AracKodu, string? CariAdi, long Bitis, int KalanGun)
{
    public string Metin => KalanGun < 0
        ? $"{AracKodu}: kira bitişi {-KalanGun} gün önce geçti ({DateTimeOffset.FromUnixTimeMilliseconds(Bitis).UtcDateTime:dd.MM.yyyy}) — iade edildiyse \"Kiralamayı Bitir\"."
        : $"{AracKodu}: kira {KalanGun} gün sonra bitiyor ({DateTimeOffset.FromUnixTimeMilliseconds(Bitis).UtcDateTime:dd.MM.yyyy})" + (CariAdi is null ? "" : $" · {CariAdi}");
}

/// <summary>
/// ═══ M-39 (AR-GE 2026-10-19, 05 F-084, kullanıcı kararı K-19 "a + çift yön") — KİRALIK MAKİNE SÖZLEŞMESİ ═══
/// Kiralayan (ya da kiraya verdiğimiz) taraf CARİ karta bağlanır, yön seçilir (kiralandı = maliyet · kiraya verildi = gelir).
/// K-19 gereği <b>otomatik cari borç/alacak YAZILMAZ</b> (fatura ile girilir); bu servis dönem maliyet/gelir hesabını ve bitiş
/// uyarısını verir. Hesap: gün birimi → dönemle kesişen gün sayısı; ay → gün/30; saat → dönemdeki geçerli sayaç okumalarının
/// farkı (saat sayaçlı makine). Fiyatsız kiralama hesapta 0 tutar görünür (eksik bilgi kullanıcıya görünsün).
/// </summary>
public sealed class KiraService
{
    public const string Iceri = "in";
    public const string Disari = "out";
    public const int UyariGun = 15;
    private const string Modul = "vehicles";
    private const long G = 86_400_000L;
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly SayacService _sayac;

    public KiraService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
        _sayac = new SayacService(factory, _clock);
    }

    private static bool Destekli(DbConnection conn) => DbIntrospect.ColumnExists(conn, null, "vehicles", "rental_direction");

    /// <summary>Sözleşme tarafı ve yönü (araç kiralık olmalı; cari bu firmanın olmalı). İşlem geçmişine yazılır.</summary>
    public void SozlesmeAyarla(SessionContext s, string vehicleId, string? partyId, string yon)
    {
        AccessControl.Require(s, Modul, PermissionAction.Edit);
        if (yon is not (Iceri or Disari)) throw new ArgumentException("Kira yönü geçersiz (kiralandı / kiraya verildi).");
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı kira sözleşmesi bilgisini henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginTransaction();
        if (!string.IsNullOrWhiteSpace(partyId))
            using (var p = conn.CreateCommand())
            {
                p.Transaction = tx;
                p.CommandText = "SELECT COUNT(*) FROM parties WHERE id=@p AND company_id=@c AND is_deleted=0;";
                p.AddWithValue("@p", partyId);
                p.AddWithValue("@c", s.CompanyId);
                if (Convert.ToInt64(p.ExecuteScalar()) == 0) throw new ArgumentException("Cari bulunamadı veya bu firmaya ait değil.");
            }
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE vehicles SET rental_party_id=@p, rental_direction=@y, updated_at=@now, version=version+1 " +
                              "WHERE id=@v AND company_id=@c AND is_deleted=0 AND is_rental=1;";
            cmd.AddWithValue("@p", string.IsNullOrWhiteSpace(partyId) ? DBNull.Value : partyId);
            cmd.AddWithValue("@y", yon);
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@v", vehicleId);
            cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new ForbiddenException("Kiralık araç bulunamadı veya başka firmaya ait.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "vehicle", vehicleId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"kiraCari\":\"{partyId}\",\"kiraYon\":\"{yon}\"}}"), _clock);
        tx.Commit();
    }

    private sealed record Kira(string Id, string Kod, string? Cari, string Yon, string Birim, decimal Fiyat, long? Bas, long? Bit, string Durum);

    private static List<Kira> Kiralar(DbConnection conn, string companyId)
    {
        var yon = Destekli(conn) ? "v.rental_direction" : "NULL";
        var cari = Destekli(conn) ? "pa.title" : "NULL";
        var join = Destekli(conn) ? "LEFT JOIN parties pa ON pa.id=v.rental_party_id AND pa.company_id=v.company_id" : "";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT v.id, v.internal_code, COALESCE({cari}, v.rental_company), COALESCE({yon},'in'), v.rental_price_unit, v.rental_price, " +
                          $"v.rental_start, v.rental_end, v.status FROM vehicles v {join} WHERE v.company_id=@c AND v.is_deleted=0 AND v.is_rental=1;";
        cmd.AddWithValue("@c", companyId);
        var list = new List<Kira>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new Kira(r.GetString(0), r.GetString(1), r.IsDBNull(2) || r.GetString(2).Length == 0 ? null : r.GetString(2), r.GetString(3),
                RentalPriceUnits.Normalize(r.IsDBNull(4) ? null : r.GetString(4)), r.IsDBNull(5) || r.GetString(5).Length == 0 ? 0m : Money.Parse(r.GetString(5)),
                r.IsDBNull(6) ? null : Convert.ToInt64(r.GetValue(6)), r.IsDBNull(7) ? null : Convert.ToInt64(r.GetValue(7)), r.GetString(8)));
        return list;
    }

    /// <summary>Dönem maliyet/gelir tablosu: [<paramref name="bas"/>, <paramref name="bit"/>] (UTC gün başı, bit dahil).</summary>
    public IReadOnlyList<KiraDonemSatiri> DonemRaporu(SessionContext s, long bas, long bit)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        if (bit < bas) throw new ArgumentException("Bitiş tarihi başlangıçtan önce olamaz.");
        using var conn = _factory.Create();
        var sonuc = new List<KiraDonemSatiri>();
        foreach (var k in Kiralar(conn, s.CompanyId))
        {
            var kBas = Math.Max(bas, k.Bas ?? bas);
            var kBit = Math.Min(bit, k.Bit ?? bit);
            if (kBit < kBas) continue;   // dönemle kesişmiyor
            decimal miktar = k.Birim switch
            {
                RentalPriceUnits.Hour => SaatKullanimi(s, k.Id, kBas, kBit),
                RentalPriceUnits.Month => Math.Round(((kBit - kBas) / G + 1) / 30m, 2),
                _ => (kBit - kBas) / G + 1,
            };
            sonuc.Add(new KiraDonemSatiri(k.Id, k.Kod, k.Cari, k.Yon, k.Birim, k.Fiyat, miktar, Math.Round(miktar * k.Fiyat, 2), k.Bit));
        }
        return sonuc.OrderBy(x => x.Yon).ThenBy(x => x.AracKodu).ToList();
    }

    /// <summary>Saatlik kirada dönemdeki çalışma saati = dönem içindeki geçerli okumaların en büyüğü − en küçüğü.</summary>
    private decimal SaatKullanimi(SessionContext s, string vehicleId, long bas, long bit)
    {
        var okumalar = _sayac.Gecmis(s, vehicleId).Where(x => !x.Gecersiz && x.Tarih >= bas && x.Tarih < bit + G).Select(x => x.Deger).ToList();
        return okumalar.Count < 2 ? 0m : okumalar.Max() - okumalar.Min();
    }

    /// <summary>Bitişi <paramref name="gun"/> gün içinde olan ya da geçmiş, hâlâ aktif (pasife alınmamış) kiralamalar.</summary>
    public IReadOnlyList<KiraBitisUyarisi> BitisUyarilari(SessionContext s, int gun = UyariGun)
    {
        AccessControl.Require(s, Modul, PermissionAction.View);
        var bugun = _clock.UtcNow.ToUnixTimeMilliseconds();
        bugun -= bugun % G;
        using var conn = _factory.Create();
        return Kiralar(conn, s.CompanyId)
            .Where(k => k.Bit is not null && k.Durum != VehicleStatus.Passive && k.Bit.Value <= bugun + gun * G)
            .Select(k => new KiraBitisUyarisi(k.Id, k.Kod, k.Cari, k.Bit!.Value, (int)((k.Bit.Value - bugun) / G)))
            .OrderBy(x => x.KalanGun).ToList();
    }
}
