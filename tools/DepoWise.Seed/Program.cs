using System.Globalization;
using Microsoft.Data.Sqlite;

namespace DepoWise.Seed;

/// <summary>
/// ═══ YÜK TESTİ TOHUMLAMA ARACI ═══ (kullanıcı isteği 2026-09-06)
///
/// <para>Kayıt girilebilen ekranların tablolarını N kayıtla doldurur ki "tablolar dolu olduğunda
/// sistem nerede hataya düşüyor" görülebilsin.</para>
///
/// <para><b>Neden şema koda gömülü değil.</b> Sütunlar <c>PRAGMA table_info</c> ile veritabanından
/// OKUNUR. Böylece migration'lar değiştikçe bu araç bozulmaz ve "test aracı gerçeği yansıtmıyor"
/// durumu oluşmaz. Değer üretimi sütun ADINA ve TİPİNE göre yapılır.</para>
///
/// <para><b>Güvenlik kapısı:</b> yalnız yolu içinde <c>artifacts</c> geçen bir SQLite dosyasında
/// çalışır. Üretim veritabanı PostgreSQL'dir ve bu araç ona zaten bağlanamaz; ek olarak yol kontrolü
/// yanlışlıkla başka bir yerel veritabanına yazılmasını da engeller.</para>
///
/// <para>Kullanım: <c>dotnet run --project tools/DepoWise.Seed -- &lt;db-yolu&gt; &lt;firma-id&gt; &lt;adet&gt; [tablo,tablo…]</c></para>
/// </summary>
internal static class Program
{
    /// <summary>Kayıt girilebilen + listeleyen ekranların tabloları (kullanıcının kastettiği ekranlar).</summary>
    private static readonly string[] VarsayilanTablolar =
    {
        "materials", "vehicles", "personnel", "parties", "equipment",
        "daily_activities", "stock_movements", "fuel_distributions",
        "invoices", "work_orders", "announcements", "projects",
    };

    private static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Kullanım: <db-yolu> <firma-id> <adet> [tablo,tablo…]");
            return 2;
        }

        // Yardımcı kip: "--sql <db> <ifade>" → tek SQL çalıştırır.
        // Tanı için gereklidir: bu makinede sqlite3 komut satırı YOK, ve bir hatanın kök nedenini
        // bulmak için veriye bakmak gerekir (ör. "hangi satırda alan boş kalmış?").
        // Aynı güvenlik kapısından geçer: yalnız artifacts altındaki test veritabanı.
        if (args[0] == "--sql")
        {
            var sqlDb = args[1];
            if (!Path.GetFullPath(sqlDb).Replace('\\', '/').Contains("/artifacts/", StringComparison.OrdinalIgnoreCase))
            { Console.Error.WriteLine("REDDEDİLDİ: yalnız artifacts altındaki test veritabanı."); return 3; }

            using var sqlConn = new SqliteConnection($"Data Source={sqlDb};Cache=Private");
            sqlConn.Open();
            using var sqlCmd = sqlConn.CreateCommand();
            sqlCmd.CommandText = args[2];
            using var sqlReader = sqlCmd.ExecuteReader();
            while (sqlReader.Read())
            {
                var parcalar = new List<string>();
                for (int c = 0; c < sqlReader.FieldCount; c++)
                    parcalar.Add(sqlReader.IsDBNull(c) ? "" : sqlReader.GetValue(c).ToString() ?? "");
                Console.WriteLine(string.Join(" | ", parcalar));
            }
            Console.WriteLine($"(etkilenen/okunan: {sqlReader.RecordsAffected})");
            return 0;
        }

        var db = args[0];
        var companyId = args[1];
        if (!int.TryParse(args[2], out var adet) || adet < 1) { Console.Error.WriteLine("adet geçersiz"); return 2; }
        var tablolar = args.Length > 3 ? args[3].Split(',', StringSplitOptions.RemoveEmptyEntries) : VarsayilanTablolar;

        // ⚠️ GÜVENLİK KAPISI — yanlışlıkla başka bir veritabanına yazılmasın.
        // Göreli yol verilebilir; kontrol MUTLAK yol üzerinden yapılır ki "artifacts/…" da geçsin.
        var normalYol = Path.GetFullPath(db).Replace('\\', '/');
        if (!normalYol.Contains("/artifacts/", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("REDDEDİLDİ: bu araç yalnız 'artifacts' altındaki TEST veritabanlarında çalışır.");
            return 3;
        }
        if (!File.Exists(db)) { Console.Error.WriteLine("Veritabanı yok: " + db); return 3; }

        using var conn = new SqliteConnection($"Data Source={db};Cache=Private");
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=OFF; PRAGMA journal_mode=WAL; PRAGMA synchronous=OFF;";
            pragma.ExecuteNonQuery();
        }

        var toplam = 0;
        foreach (var tablo in tablolar)
        {
            try
            {
                var eklendi = Doldur(conn, tablo.Trim(), companyId, adet);
                toplam += eklendi;
                Console.WriteLine($"{tablo,-22} +{eklendi}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{tablo,-22} ATLANDI — {ex.Message.Split('\n')[0]}");
            }
        }
        Console.WriteLine($"TOPLAM EKLENEN = {toplam}");
        return 0;
    }

    private sealed record Sutun(string Ad, string Tip, bool NotNull, bool Pk);

    private static List<Sutun> Sutunlar(SqliteConnection conn, string tablo)
    {
        var liste = new List<Sutun>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tablo});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            liste.Add(new Sutun(r.GetString(1), r.GetString(2).ToUpperInvariant(), r.GetInt32(3) == 1, r.GetInt32(5) == 1));
        if (liste.Count == 0) throw new InvalidOperationException("tablo yok");
        return liste;
    }

    /// <summary>Tablodan var olan bir kimlik çeker (ilişkili sütunları gerçek kayda bağlamak için).</summary>
    private static string? BirId(SqliteConnection conn, string tablo, string companyId)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT id FROM {tablo} WHERE company_id=@c LIMIT 1;";
            cmd.Parameters.AddWithValue("@c", companyId);
            return cmd.ExecuteScalar() as string;
        }
        catch { return null; }
    }

    private static int Doldur(SqliteConnection conn, string tablo, string companyId, int hedef)
    {
        var sutunlar = Sutunlar(conn, tablo);

        // Kaç kayıt VAR? Hedefe tamamlanır — araç iki kez çalıştırılırsa 20.000 olmaz.
        int mevcut;
        using (var say = conn.CreateCommand())
        {
            say.CommandText = $"SELECT COUNT(*) FROM {tablo} WHERE company_id=@c;";
            say.Parameters.AddWithValue("@c", companyId);
            mevcut = Convert.ToInt32(say.ExecuteScalar());
        }
        var eklenecek = hedef - mevcut;
        if (eklenecek <= 0) return 0;

        // İlişkili sütunlar için gerçek kimlikler (yoksa null bırakılır).
        var partyId = BirId(conn, "parties", companyId);
        var vehicleId = BirId(conn, "vehicles", companyId);
        var materialId = BirId(conn, "materials", companyId);
        var personnelId = BirId(conn, "personnel", companyId);
        string? branchId;
        using (var b = conn.CreateCommand())
        {
            b.CommandText = "SELECT id FROM branches WHERE company_id=@c AND kind<>'company' LIMIT 1;";
            b.Parameters.AddWithValue("@c", companyId);
            branchId = b.ExecuteScalar() as string;
        }

        var yazilabilir = sutunlar.Where(s => !s.Pk || s.Ad == "id").ToList();
        var alanlar = string.Join(",", yazilabilir.Select(s => s.Ad));
        var yerler = string.Join(",", yazilabilir.Select(s => "@" + s.Ad));

        var simdi = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"INSERT INTO {tablo}({alanlar}) VALUES({yerler});";
        foreach (var s in yazilabilir) cmd.Parameters.Add(new SqliteParameter("@" + s.Ad, DBNull.Value));
        cmd.Prepare();

        for (int i = 0; i < eklenecek; i++)
        {
            var sira = mevcut + i + 1;
            foreach (var s in yazilabilir)
                cmd.Parameters["@" + s.Ad].Value =
                    Deger(s, sira, companyId, simdi, branchId, partyId, vehicleId, materialId, personnelId) ?? DBNull.Value;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return eklenecek;
    }

    /// <summary>
    /// Sütun için değer üretir. Öncelik sırası: bilinen ANLAMLI adlar → tip → NOT NULL zorunluluğu.
    /// Boş bırakılabilen (nullable) sütunlar bilinçli olarak boş bırakılır: gerçek veride de
    /// çoğu opsiyonel alan boştur ve ekranların bu duruma dayanması test edilmelidir.
    /// </summary>
    private static object? Deger(Sutun s, int sira, string companyId, long simdi,
        string? branchId, string? partyId, string? vehicleId, string? materialId, string? personnelId)
    {
        var ad = s.Ad;
        var no = sira.ToString("D5", CultureInfo.InvariantCulture);

        switch (ad)
        {
            case "id": return Guid.NewGuid().ToString("N");
            case "company_id": return companyId;
            case "branch_id": case "op_branch_id": case "location_branch_id": return branchId;
            case "party_id": return partyId;
            case "vehicle_id": return vehicleId;
            case "material_id": return materialId;
            case "personnel_id": case "driver_personnel_id": case "technician_id": return personnelId;
            case "created_at": case "updated_at": return simdi;
            case "version": return 1L;
            case "is_deleted": case "is_reversed": case "is_cancelled": return 0L;
            case "is_active": return 1L;
            case "code": case "internal_code": return "YUK-" + no;
            case "plate": return "34YK" + no;
            case "title": return "Yük Testi Kaydı " + no;
            case "name": case "full_name": case "material_name": return "Yük Testi " + no;
            case "party_type": return "customer";
            case "currency_code": return "TRY";
            case "amount": case "unit_price": return "100.00";
            case "direction": return (long)(sira % 2 == 0 ? 1 : -1);
            case "doc_type": return "manual";
            case "entry_date": case "distribution_date": case "activity_date": case "doc_date":
            case "invoice_date": case "movement_date": return simdi;
            case "status": return "active";
            case "type": case "kind": return "other";
        }

        // Ad eşleşmediyse: NULL kabul eden sütun BOŞ bırakılır (gerçek veriye benzesin).
        if (!s.NotNull) return null;

        // NOT NULL ise tipe göre güvenli bir değer üret.
        return s.Tip switch
        {
            "BIGINT" or "INTEGER" or "INT" => 0L,
            "REAL" or "NUMERIC" or "DECIMAL" => 0.0,
            _ => ad.EndsWith("_at") ? simdi.ToString(CultureInfo.InvariantCulture) : ad + "-" + no,
        };
    }
}
