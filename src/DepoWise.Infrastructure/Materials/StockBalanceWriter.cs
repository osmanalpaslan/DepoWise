using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;
using System.Data.Common;

namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// YARIŞ DURUMU (race condition) sinyali: bakiye satırı, biz okuduktan sonra BAŞKA bir işlem tarafından
/// değiştirildi. İÇ sinyaldir — <see cref="StockBalanceWriter.Run{T}"/> yakalar ve işlemi baştan dener.
/// Kullanıcıya asla bu mesaj gösterilmez (bkz. <see cref="StockBusyException"/>).
/// </summary>
public sealed class StockConcurrencyException : Exception
{
    public StockConcurrencyException(string message) : base(message) { }
}

/// <summary>
/// Tekrar hakkı tükendi: aynı malzeme üzerinde ısrarla eşzamanlı işlem var. Kullanıcıya gösterilen
/// TEKNİK OLMAYAN mesajı taşır; teknik ayrıntı loga yazılır (kullanıcı kararı 2026-08-08, K-5).
/// </summary>
public sealed class StockBusyException : Exception
{
    public StockBusyException(string message) : base(message) { }
}

/// <summary>
/// STOK BAKİYESİNİN TEK ORTAK YAZICISI (Faz 3-Ön, kullanıcı kararları 2026-08-08).
///
/// NEDEN VAR: <c>stock_balances</c> güncellemesi "oku → kontrol et → yaz" desenindeydi ve mutlak değeri
/// yazıyordu. SQLite'ta <c>BeginImmediate</c> (IMMEDIATE) tek yazara izin verdiği için sorun yoktu; ancak
/// PostgreSQL'de transaction READ COMMITTED olduğundan iki eşzamanlı çıkış aynı bakiyeyi okuyup ikisi de
/// "yeterli" görebiliyor, sonra biri diğerinin düşümünü eziyordu (oversell + bakiye kaybı).
///
/// ÇÖZÜM — İYİMSER CAS (compare-and-swap): yazma koşuluna "okuduğum değer HÂLÂ aynı mı" konur. Değer
/// değiştiyse 0 satır etkilenir → <see cref="StockConcurrencyException"/> → çağıran transaction geri alınır
/// ve işlem baştan denenir (<see cref="Run{T}"/>, en fazla <see cref="MaxRetries"/> tekrar).
///
/// ⚠️ EN KRİTİK DETAY — HAM METİNLE KARŞILAŞTIRMA: <c>quantity</c> kolonu TEXT'tir ve <c>Money.Serialize</c>
/// decimal ölçeğini korur (<c>10m</c> → "10", <c>10.00m</c> → "10.00" — değer eşit, METİN farklı). Koşula
/// yeniden üretilmiş bir metin konursa veritabanındakiyle tutmayabilir ve her denemede KALICI sahte çakışma
/// oluşur. Bu yüzden koşulda DAİMA veritabanından okunan ham metnin kendisi kullanılır.
/// (Kolon tanımlarında COLLATE yoktur — Migration053 yalnız sorgu ifadeleri için collation tanımlar — bu
/// yüzden metin eşitliği hem SQLite'ta hem PostgreSQL'de bayt düzeyinde kesindir.)
///
/// SQLITE DAVRANIŞI DEĞİŞMEZ: tek yazar olduğu için CAS koşulu her zaman tutar, istisna hiç fırlamaz,
/// tekrar hiç çalışmaz.
/// </summary>
public static class StockBalanceWriter
{
    /// <summary>İlk denemeden SONRA en fazla kaç tekrar (kullanıcı kararı: 3). Sonsuz döngü yoktur.</summary>
    public const int MaxRetries = 3;

    /// <summary>Kullanıcıya gösterilen mesaj — teknik terim içermez (CLAUDE.md §2).</summary>
    public const string BusyMessage =
        "İşleminiz tamamlanamadı. Bu malzeme üzerinde aynı anda başka bir işlem yapıldı. " +
        "Lütfen ekranı yenileyip tekrar deneyin.";

    /// <summary>Teknik log kanalı. Varsayılan: standart hata akışı (API'de sunucu logu). Test/masaüstü
    /// kendi kanalını takabilir. Yarış tespiti ile SİSTEM hatası burada AYRI etiketlerle yazılır.</summary>
    public static Action<string> Log { get; set; } = static m => Console.Error.WriteLine(m);

    // Kısa ve SINIRLI bekleme (agresif polling değil): çakışmada 10-40 ms. En kötü durumda toplam ~120 ms.
    private static readonly Random _jitter = new();

    /// <summary>
    /// Bakiyeye işaretli miktarı uygular (CAS). Düşüşte negatif olursa fail-closed —
    /// <see cref="NegativeStockException"/>. Bu davranış ESKİSİYLE BİREBİR AYNIDIR; yalnız yazma koşulu eklendi.
    /// </summary>
    public static void ApplyDelta(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        decimal signedQty, long now, bool allowNegative)
    {
        var raw = ReadRaw(conn, tx, materialId);          // HAM METİN (satır yoksa null)
        var current = Money.Parse(raw);
        var updated = current + signedQty;
        if (!allowNegative && updated < 0)
            throw new NegativeStockException($"Negatif stok engellendi: mevcut {current}, talep {-signedQty}.");

        int affected;
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            if (raw is null)
            {
                // Bakiye satırı henüz yok → oluştur. Araya biri girip oluşturduysa 0 satır → yarış.
                cmd.CommandText = @"
INSERT INTO stock_balances(company_id, material_id, quantity, updated_at) VALUES(@c,@m,@q,@now)
ON CONFLICT(material_id) DO NOTHING;";
                cmd.AddWithValue("@c", companyId);
            }
            else
            {
                // CAS: okuduğumuz HAM METİN hâlâ yerinde mi? Değiştiyse 0 satır → yarış.
                cmd.CommandText = @"
UPDATE stock_balances SET quantity=@q, updated_at=@now WHERE material_id=@m AND quantity=@expected;";
                cmd.AddWithValue("@expected", raw);
            }
            cmd.AddWithValue("@m", materialId);
            cmd.AddWithValue("@q", Money.Serialize(updated));
            cmd.AddWithValue("@now", now);
            affected = cmd.ExecuteNonQuery();
        }

        if (affected == 0)
            throw new StockConcurrencyException(
                $"stock_balances yarışı: material={materialId} expected='{raw ?? "(yok)"}' delta={signedQty}");
    }

    /// <summary>Bakiyeyi okur (decimal). Ham metne ihtiyaç duyan CAS için <see cref="ReadRaw"/> kullanılır.</summary>
    public static decimal ReadBalance(DbConnection conn, DbTransaction? tx, string materialId)
        => Money.Parse(ReadRaw(conn, tx, materialId));

    /// <summary>Bakiyenin veritabanındaki HAM metni (satır yoksa null). CAS koşulu bunu kullanır.</summary>
    public static string? ReadRaw(DbConnection conn, DbTransaction? tx, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE material_id=@m;";
        cmd.AddWithValue("@m", materialId);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// TEKRAR SARMALAYICISI — transaction sınırında kullanılır. İşlem YALNIZ
    /// <see cref="StockConcurrencyException"/> için tekrarlanır; başka HİÇBİR hata tekrarlanmaz
    /// (kullanıcı kararı K-5: yarış ile sistem/veritabanı hatası birbirine karışmasın):
    ///  • <see cref="NegativeStockException"/> → iş kuralı, tekrar YOK
    ///  • yetki/doğrulama hataları → tekrar YOK
    ///  • DbException, zaman aşımı, bağlantı kopması → tekrar YOK, olduğu gibi yukarı fırlar
    /// Tekrar hakkı biterse <see cref="StockBusyException"/> (kullanıcı mesajı) fırlar.
    /// </summary>
    public static T Run<T>(Func<T> action, string context)
    {
        for (int attempt = 1; ; attempt++)
        {
            try { return action(); }
            catch (StockConcurrencyException ex)
            {
                Log($"[stock-cas] conflict {context} attempt={attempt}/{MaxRetries + 1} {ex.Message}");
                if (attempt > MaxRetries)
                {
                    Log($"[stock-cas] give-up {context} ({MaxRetries} tekrar sonrası) {ex.Message}");
                    throw new StockBusyException(BusyMessage);
                }
                int wait; lock (_jitter) { wait = _jitter.Next(10, 41); }
                System.Threading.Thread.Sleep(wait);
            }
        }
    }

    /// <summary>Değer döndürmeyen işlemler için <see cref="Run{T}"/> eşleniği.</summary>
    public static void Run(Action action, string context)
        => Run<object?>(() => { action(); return null; }, context);
}
