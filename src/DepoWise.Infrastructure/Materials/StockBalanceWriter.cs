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

    /// <summary>STK-02 — lokasyonu bilinmeyen (geçmiş) stok kovası. Boş metin; NULL DEĞİL (PK kolonu).</summary>
    public const string Unassigned = "";

    // Kısa ve SINIRLI bekleme (agresif polling değil): çakışmada 10-40 ms. En kötü durumda toplam ~120 ms.
    private static readonly Random _jitter = new();

    /// <summary>
    /// Bakiyeye işaretli miktarı uygular (CAS). Düşüşte negatif olursa fail-closed —
    /// <see cref="NegativeStockException"/>. Bu davranış ESKİSİYLE BİREBİR AYNIDIR; yalnız yazma koşulu eklendi.
    /// </summary>
    /// <param name="locationId">
    /// STK-02 — stok LOKASYONU (<c>branches.id</c>). Lokasyon bilinmiyorsa <see cref="Unassigned"/> (boş metin)
    /// geçilir. <b>Varsayılan değer YOKTUR ve konulmayacaktır:</b> çağıranın hangi lokasyona yazdığını
    /// bilinçli olarak belirtmesi gerekir; aksi halde stok sessizce yanlış kovaya yazılır.
    /// Bilinmeyen lokasyon ASLA rastgele bir şubeye yazılmaz.
    /// </param>
    public static void ApplyDelta(DbConnection conn, DbTransaction tx, string companyId, string materialId,
        string locationId, decimal signedQty, long now, bool allowNegative)
    {
        locationId ??= Unassigned;
        var raw = ReadRaw(conn, tx, companyId, materialId, locationId);   // HAM METİN (satır yoksa null)
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
                // STK-02: çakışma hedefi artık BİLEŞİK birincil anahtardır.
                cmd.CommandText = @"
INSERT INTO stock_balances(company_id, material_id, location_id, quantity, updated_at) VALUES(@c,@m,@l,@q,@now)
ON CONFLICT(company_id, material_id, location_id) DO NOTHING;";
            }
            else
            {
                // CAS: okuduğumuz HAM METİN hâlâ yerinde mi? Değiştiyse 0 satır → yarış.
                cmd.CommandText = @"
UPDATE stock_balances SET quantity=@q, updated_at=@now
WHERE company_id=@c AND material_id=@m AND location_id=@l AND quantity=@expected;";
                cmd.AddWithValue("@expected", raw);
            }
            cmd.AddWithValue("@c", companyId);
            cmd.AddWithValue("@m", materialId);
            cmd.AddWithValue("@l", locationId);
            cmd.AddWithValue("@q", Money.Serialize(updated));
            cmd.AddWithValue("@now", now);
            affected = cmd.ExecuteNonQuery();
        }

        if (affected == 0)
            throw new StockConcurrencyException(
                $"stock_balances yarışı: material={materialId} location='{locationId}' expected='{raw ?? "(yok)"}' delta={signedQty}");
    }

    /// <summary>Lokasyon bakiyesini okur (decimal). CAS için ham metin: <see cref="ReadRaw"/>.</summary>
    public static decimal ReadBalance(DbConnection conn, DbTransaction? tx, string companyId, string materialId,
        string locationId)
        => Money.Parse(ReadRaw(conn, tx, companyId, materialId, locationId));

    /// <summary>TEK LOKASYONUN ham bakiye metni (satır yoksa null). CAS koşulu bunu kullanır.</summary>
    public static string? ReadRaw(DbConnection conn, DbTransaction? tx, string companyId, string materialId,
        string locationId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "SELECT quantity FROM stock_balances WHERE company_id=@c AND material_id=@m AND location_id=@l;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        cmd.AddWithValue("@l", locationId ?? Unassigned);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>
    /// STK-02 — FİRMA GENELİ toplam bakiye: malzemenin TÜM lokasyonlarının toplamı.
    /// Toplama <b>C#'ta decimal</b> ile yapılır: <c>quantity</c> TEXT içinde decimal tutulur ve
    /// SQLite'ta <c>SUM(CAST(... AS REAL))</c> kayan nokta hatası üretir (Money kuralı: float yasak).
    /// </summary>
    public static decimal ReadTotal(DbConnection conn, DbTransaction? tx, string companyId, string materialId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT quantity FROM stock_balances WHERE company_id=@c AND material_id=@m;";
        cmd.AddWithValue("@c", companyId);
        cmd.AddWithValue("@m", materialId);
        decimal total = 0m;
        using var r = cmd.ExecuteReader();
        while (r.Read()) total += Money.Parse(r.IsDBNull(0) ? null : r.GetString(0));
        return total;
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
            catch (Exception ex) when (ex is StockConcurrencyException || IsDocumentNumberRace(ex))
            {
                // İki yarış türü aynı politikayı paylaşır ama logda AYRI etiketlenir (yarış ≠ sistem hatası).
                var tag = ex is StockConcurrencyException ? "stock-cas" : "stock-docno";
                Log($"[{tag}] conflict {context} attempt={attempt}/{MaxRetries + 1} {ex.Message}");
                if (attempt > MaxRetries)
                {
                    Log($"[{tag}] give-up {context} ({MaxRetries} tekrar sonrası) {ex.Message}");
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

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // BELGE NUMARASI (doc_no) YARIŞI — PostgreSQL test bulgusu 2026-08-08, kullanıcı kararı S1
    //
    // NextDocNo, belge numarasını "MAX(doc_no) + 1" ile üretir; tek koruma
    // ux_stock_documents_no (company_id, doc_type, doc_no) BENZERSİZLİK indeksidir. PostgreSQL'de
    // eşzamanlı iki AYNI TİP belge aynı numarayı hesaplar → ikinci INSERT 23505 ile reddedilir.
    // Bu, bakiye CAS'i ile AYNI SINIFTAN bir yarıştır (veri bozulmaz, işlem tümüyle geri alınır) ve
    // aynı şekilde yeniden denenmelidir: tekrar sırasında NextDocNo bir sonraki numarayı alır ve
    // stok kontrolleri BAŞTAN çalışır.
    //
    // ⚠️ KAPSAM BİLEREK DARDIR (kullanıcı kuralı): yalnız doc_no benzersizlik ihlali yarış sayılır.
    // Genel 23505, başka benzersizlik kısıtları (ör. ux_stock_movements_operation), yabancı anahtar
    // hataları, doğrulama hataları ve gerçek sistem/veritabanı arızaları TEKRARLANMAZ.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>PostgreSQL'in bu kısıt için ürettiği metin (indeks adını içerir).</summary>
    private const string PgDocNoConstraint = "ux_stock_documents_no";

    /// <summary>SQLite'ın aynı ihlal için ürettiği metin (indeks adı yerine kolonu yazar).</summary>
    private const string SqliteDocNoColumn = "stock_documents.doc_no";

    /// <summary>
    /// Bu veritabanı hatası, BELGE NUMARASI çakışması mı (yani yeniden denenebilir bir yarış mı)?
    /// Lehçeye özel tip kullanılmaz (Infrastructure Npgsql'e bağımlı değildir); iki veritabanının da
    /// ürettiği ayırt edici metin aranır. Başka hiçbir hata bu koşulu sağlamaz.
    /// </summary>
    public static bool IsDocumentNumberRace(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is not DbException) continue;
            var m = e.Message;
            if (m.Contains(PgDocNoConstraint, StringComparison.OrdinalIgnoreCase) ||
                m.Contains(SqliteDocNoColumn, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
