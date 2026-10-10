using System.Data.Common;

namespace DepoWise.Infrastructure.Database.Migrations;

/// <summary>
/// ═══ KİRALIK ARAÇLAR + ARAÇ DEĞİŞİMİ (kullanıcı isteği 2026-10-10) ═══
///
/// <b>Kullanıcının isteği:</b> <i>"Şirkette bazı araçlar kiralık olabiliyor. Bu araçların mevcut araç
/// listesi ekranında listelenmesini istemiyorum. Kiralık araçlar adında yeni bir ekran ve tablosu
/// olmalı… depo çıkışı yapabilirim veya bakımı bizim şantiyemizde yapılmak zorunda kalabilir… ilgili
/// ekranlarda iki tablonun da verilerini listeleyebilmeliyim… araç arızalandığında arızalı aracı alıp
/// yerine farklı bir araç verebiliyorlar. Bu değişimin kaydını yeni aracı oluştururken girebilmeliyim
/// ve giden aracı kiralık listesinde otomatik bir şekilde pasife almalı."</i>
///
/// <b>NEDEN AYRI FİZİKSEL TABLO DEĞİL.</b> Yakıt, bakım, muayene, stok çıkışı, günlük faaliyet, sayaç
/// geçmişi, uyumlu malzeme ve talep kalemleri <c>vehicles(id)</c>'ye yabancı anahtarla bağlıdır. Kiralık
/// araç ayrı bir tabloda dursaydı bu ekranların HER BİRİ iki tabloya birden bakacak şekilde yeniden
/// yazılmak zorunda kalırdı ve "iki tablonun verisini birlikte listele" isteği her ekranda ayrı ayrı
/// birleştirme kodu demekti. Bunun yerine kiralık araç aynı tabloda <c>is_rental=1</c> ile işaretlenir;
/// kullanıcı için AYRI EKRAN ve AYRI LİSTE (tablo) olarak sunulur, Araç Listesi onları göstermez, diğer
/// ekranların seçicileri ise ikisini birlikte ("Kiralık" etiketiyle) listeler. Böylece yakıt/bakım/stok
/// gibi tüm mevcut akışlar kiralık araçta da DEĞİŞİKLİKSİZ çalışır.
///
/// <b>Eklenen sütunlar (vehicles):</b>
/// <list type="bullet">
///   <item><c>is_rental</c> — 0 = şirket aracı (varsayılan, tüm mevcut kayıtlar), 1 = kiralık.</item>
///   <item><c>rental_company</c> — kiralayan firma (serbest metin).</item>
///   <item><c>rental_start</c> / <c>rental_end</c> — kira başlangıç / bitiş günü (UTC gün başı, ms).</item>
///   <item><c>rental_price</c> + <c>rental_price_unit</c> — kira bedeli (decimal metin) ve birimi
///         (hour/day/month).</item>
///   <item><c>replaced_vehicle_id</c> — bu aracın YERİNE GELDİĞİ (iade edilen) kiralık araç.</item>
///   <item><c>replacement_reason</c> — değişim nedeni (ör. "motor arızası").</item>
/// </list>
///
/// <b>CANLI VERİ GÜVENLİĞİ:</b> yalnız <c>ADD COLUMN</c>. <c>is_rental</c> varsayılanı 0 olduğu için
/// mevcut tüm araçlar "şirket aracı" kalır ve Araç Listesi'nde aynen görünür. UPDATE/DELETE yok.
/// Geri alma: sekiz <c>DROP COLUMN</c>.
///
/// <b>SENKRON:</b> <c>vehicles</c> zaten senkronda; yeni sütunlar alan kesişimiyle taşınır. Eski bir
/// masaüstü bu sütunları göndermez → sunucudaki değerleri EZMEZ (genel upsert yalnız gelen alanları yazar).
/// </summary>
public sealed class Migration098_RentalVehicles : IMigration
{
    public int Version => 98;
    public string Name => "rental_vehicles";

    private static readonly (string Column, string Ddl)[] Sutunlar =
    {
        ("is_rental",           "BIGINT NOT NULL DEFAULT 0"),
        ("rental_company",      "TEXT NULL"),
        ("rental_start",        "BIGINT NULL"),
        ("rental_end",          "BIGINT NULL"),
        ("rental_price",        "TEXT NULL"),
        ("rental_price_unit",   "TEXT NULL"),
        ("replaced_vehicle_id", "TEXT NULL"),
        ("replacement_reason",  "TEXT NULL"),
    };

    public void Up(DbConnection conn, DbTransaction tx)
    {
        foreach (var (sutun, ddl) in Sutunlar)
        {
            if (DbIntrospect.ColumnExists(conn, tx, "vehicles", sutun)) continue;
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"ALTER TABLE vehicles ADD COLUMN {sutun} {ddl};";
            cmd.ExecuteNonQuery();
        }
    }
}
