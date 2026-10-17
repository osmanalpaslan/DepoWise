using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Sync;

/// <summary>Sunucunun reddettiği tek satır (push yanıtındaki <c>rejected</c> öğesi).</summary>
public sealed record RejectedRow(string Table, string Key, string Reason, bool Permanent);

/// <summary>Ekranda gösterilen gönderilemeyen kayıt.</summary>
public sealed record GonderilemeyenKayit(string Anahtar, string Tablo, string TabloEtiketi, string SatirAnahtari,
    string Tanim, string Neden, bool Kalici, long IlkZaman, long SonZaman, int Deneme, bool YereldeVar)
{
    /// <summary>Kart başlığı: "Malzeme · R-1 · Çimento".</summary>
    public string Baslik => $"{TabloEtiketi} · {Tanim}";

    /// <summary>Sunucu nedeni "tablo: ..." biçimindedir; teknik tablo adı kullanıcıya gösterilmez.</summary>
    public string NedenMetni => "Neden: " + (Neden.StartsWith(Tablo + ":", StringComparison.Ordinal) ? Neden[(Tablo.Length + 1)..].Trim() : Neden);

    public string ZamanMetni
    {
        get
        {
            static string T(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
            return Deneme > 1 ? $"{T(IlkZaman)}'den beri · {Deneme} deneme" : T(SonZaman);
        }
    }

    public string Ipucu => !YereldeVar
        ? "Bu kayıt artık bu bilgisayarda yok; listeden kaldırabilirsiniz."
        : Kalici
            ? "Bu neden kendiliğinden düzelmez: kaydı açıp sorunu giderin (ör. kodu değiştirin, bağlı kaydı seçin) ve kaydedin."
            : "Geçici olabilir; 'Yeniden Gönder' ile tekrar deneyebilirsiniz.";
}

/// <summary>
/// ═══ M-15 (AR-GE 2026-10-17, B1 adım 4) — GÖNDERİLEMEYEN KAYITLAR (masaüstü, Migration101) ═══
///
/// Her gönderimden sonra <see cref="Isle"/> çağrılır: sunucunun reddettiği satırlar listeye yazılır/güncellenir, aynı turda
/// GÖNDERİLİP KABUL EDİLEN satırların eski kayıtları silinir (sorun çözülmüş). Liste imleçten bağımsızdır: kayıt imlecin
/// gerisinde kalsa da kaybolmaz. <see cref="YenidenGonder"/> satırı yeniden "değişti" işaretler (yerel sayaç, Migration100)
/// → bir sonraki turda tekrar gider. <see cref="Yoksay"/> yalnız listeden gizler; veriye dokunmaz.
/// </summary>
public sealed class GonderimRedleriService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public GonderimRedleriService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    private static readonly Dictionary<string, string> Etiketler = new(StringComparer.Ordinal)
    {
        ["materials"] = "Malzeme", ["vehicles"] = "Araç", ["equipment"] = "Ekipman", ["personnel"] = "Personel",
        ["suppliers"] = "Tedarikçi", ["brands"] = "Marka", ["units"] = "Birim", ["material_categories"] = "Malzeme kategorisi",
        ["vehicle_types"] = "Araç tipi", ["vehicle_categories"] = "Araç kategorisi", ["vehicle_models"] = "Araç modeli",
        ["equipment_types"] = "Ekipman türü", ["cost_centers"] = "Maliyet merkezi", ["maintenance_definitions"] = "Bakım tanımı",
        ["material_templates"] = "Malzeme şablonu", ["vehicle_templates"] = "Araç şablonu",
        ["vehicle_template_materials"] = "Şablon malzemesi", ["vehicle_inspections"] = "Muayene/sigorta",
        ["vehicle_meter_logs"] = "Sayaç kaydı", ["material_equivalents"] = "Muadil eşleşmesi",
        ["material_compatible_vehicles"] = "Uyumlu araç eşleşmesi", ["vehicle_maintenances"] = "Araç bakımı",
        ["maintenance_materials"] = "Bakım malzemesi", ["equipment_maintenances"] = "Ekipman bakımı",
        ["equipment_maintenance_materials"] = "Ekipman bakım malzemesi", ["equipment_inspections"] = "Ekipman muayenesi",
        ["fuel_depot_entries"] = "Yakıt deposu girişi", ["fuel_distributions"] = "Yakıt dağıtımı",
        ["daily_activities"] = "Günlük faaliyet", ["stock_movements"] = "Stok hareketi", ["stock_documents"] = "Stok belgesi",
        ["stock_count_lines"] = "Sayım satırı", ["assignment_movements"] = "Zimmet hareketi",
        ["material_requests"] = "Malzeme talebi", ["material_request_items"] = "Talep kalemi",
        ["purchase_orders"] = "Satın alma siparişi", ["purchase_order_lines"] = "Sipariş satırı", ["work_orders"] = "İş emri",
        ["calendar_events"] = "Takvim kaydı", ["announcements"] = "Duyuru", ["parties"] = "Cari", ["party_ledger"] = "Cari hareketi",
        ["invoices"] = "Fatura", ["invoice_lines"] = "Fatura satırı",
    };

    public static string Etiket(string table) => Etiketler.TryGetValue(table, out var e) ? e : table;

    private static string Anahtar(string companyId, string table, string key) => $"{companyId}|{table}|{key}";

    /// <summary>Satırın birincil anahtar değerlerini "|" ile birleştirir (sunucudaki kayıtla AYNI kural).</summary>
    public static string SatirAnahtari(JsonElement row, IReadOnlyList<string> pk)
        => string.Join("|", pk.Select(k => row.TryGetProperty(k, out var v) && v.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : ""));

    /// <summary>Bir gönderim turunun sonucunu işler. <paramref name="gonderilenPaket"/> = sunucuya giden JSON.</summary>
    public void Isle(string companyId, string gonderilenPaket, IReadOnlyList<RejectedRow> redler)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!DbIntrospect.TableExists(conn, null, "sync_rejections")) return;
        using var tx = conn.BeginTransaction();
        var redAnahtarlari = new HashSet<string>(redler.Select(r => Anahtar(companyId, r.Table, r.Key)), StringComparer.Ordinal);

        // 1) Kabul edilenlerin eski kaydını sil (sorun çözüldü).
        using (var doc = JsonDocument.Parse(gonderilenPaket))
        {
            if (doc.RootElement.TryGetProperty("tables", out var tablolar) && tablolar.ValueKind == JsonValueKind.Object)
                foreach (var t in tablolar.EnumerateObject())
                {
                    if (t.Value.ValueKind != JsonValueKind.Array || t.Value.GetArrayLength() == 0) continue;
                    if (!BusinessSyncService.Tables.Contains(t.Name) || !DbIntrospect.TableExists(conn, tx, t.Name)) continue;
                    var pk = DbIntrospect.PrimaryKey(conn, t.Name);
                    if (pk.Count == 0) continue;
                    foreach (var row in t.Value.EnumerateArray())
                    {
                        var a = Anahtar(companyId, t.Name, SatirAnahtari(row, pk));
                        if (redAnahtarlari.Contains(a)) continue;
                        using var del = conn.CreateCommand();
                        del.Transaction = tx;
                        del.CommandText = "DELETE FROM sync_rejections WHERE anahtar=@a;";
                        del.AddWithValue("@a", a);
                        del.ExecuteNonQuery();
                    }
                }
        }

        // 2) Reddedilenleri yaz / güncelle (yoksayılmış olan yoksayılmış kalır; yalnız bilgi güncellenir).
        foreach (var r in redler)
        {
            using var up = conn.CreateCommand();
            up.Transaction = tx;
            up.CommandText = """
                INSERT INTO sync_rejections(anahtar, company_id, table_name, row_key, reason, permanent, first_at, last_at, attempts, status)
                VALUES(@a, @c, @t, @k, @r, @p, @n, @n, 1, 'open')
                ON CONFLICT(anahtar) DO UPDATE SET reason=excluded.reason, permanent=excluded.permanent,
                    last_at=excluded.last_at, attempts=sync_rejections.attempts + 1;
                """;
            up.AddWithValue("@a", Anahtar(companyId, r.Table, r.Key));
            up.AddWithValue("@c", companyId);
            up.AddWithValue("@t", r.Table);
            up.AddWithValue("@k", r.Key);
            up.AddWithValue("@r", r.Reason.Length > 500 ? r.Reason[..500] : r.Reason);
            up.AddWithValue("@p", r.Permanent ? 1 : 0);
            up.AddWithValue("@n", now);
            up.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public int AcikSayisi(string companyId)
    {
        using var conn = _factory.Create();
        if (!DbIntrospect.TableExists(conn, null, "sync_rejections")) return 0;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sync_rejections WHERE company_id=@c AND status='open';";
        cmd.AddWithValue("@c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>Açık (yoksayılmamış) kayıtlar, en yeni önce; her biri için yereldeki kaydın kısa tanımıyla.</summary>
    public IReadOnlyList<GonderilemeyenKayit> Acik(string companyId)
    {
        using var conn = _factory.Create();
        var liste = new List<GonderilemeyenKayit>();
        if (!DbIntrospect.TableExists(conn, null, "sync_rejections")) return liste;
        var ham = new List<(string A, string T, string K, string R, bool P, long F, long L, int N)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT anahtar, table_name, row_key, reason, permanent, first_at, last_at, attempts " +
                              "FROM sync_rejections WHERE company_id=@c AND status='open' ORDER BY last_at DESC;";
            cmd.AddWithValue("@c", companyId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                ham.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt64(4) != 0,
                         r.GetInt64(5), r.GetInt64(6), r.GetInt32(7)));
        }
        foreach (var h in ham)
        {
            var (yerelde, tanim) = Tanimla(conn, h.T, h.K);
            liste.Add(new GonderilemeyenKayit(h.A, h.T, Etiket(h.T), h.K, tanim, h.R, h.P, h.F, h.L, h.N, yerelde));
        }
        return liste;
    }

    /// <summary>Yereldeki satırı okunur kısa metne çevirir (kod/ad/belge no…); yoksa anahtar.</summary>
    private static (bool Var, string Tanim) Tanimla(System.Data.Common.DbConnection conn, string table, string key)
    {
        if (!BusinessSyncService.Tables.Contains(table) || !DbIntrospect.TableExists(conn, null, table)) return (false, key);
        var pk = DbIntrospect.PrimaryKey(conn, table);
        var parcalar = key.Split('|');
        if (pk.Count == 0 || pk.Count != parcalar.Length) return (false, key);
        var cols = DbIntrospect.ColumnNames(conn, table);
        var goster = new[] { "code", "plate", "document_no", "invoice_no", "name", "title", "full_name", "description" }
            .Where(cols.Contains).Take(2).ToList();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {(goster.Count > 0 ? string.Join(",", goster) : "1")} FROM {table} WHERE " +
                          string.Join(" AND ", pk.Select((k, i) => $"{k}=@k{i}")) + " LIMIT 1;";
        for (int i = 0; i < pk.Count; i++) cmd.AddWithValue($"@k{i}", parcalar[i]);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (false, key);
        var metin = string.Join(" · ", Enumerable.Range(0, goster.Count).Where(i => !r.IsDBNull(i)).Select(i => r.GetValue(i).ToString()));
        return (true, string.IsNullOrWhiteSpace(metin) ? key : metin);
    }

    /// <summary>Satırı yeniden "değişti" işaretler → bir sonraki gönderimde tekrar gider. Yerelde yoksa false.</summary>
    public bool YenidenGonder(string companyId, string anahtar)
    {
        using var conn = _factory.Create();
        var (table, key) = Ayir(conn, companyId, anahtar);
        if (table is null || !DbIntrospect.ColumnExists(conn, null, table, "local_change_seq")) return false;
        var pk = DbIntrospect.PrimaryKey(conn, table);
        var parcalar = key!.Split('|');
        if (pk.Count == 0 || pk.Count != parcalar.Length) return false;
        using var cmd = conn.CreateCommand();
        // Değeri değişmeyen güncelleme de yerel sayaç tetikleyicisini çalıştırır (Migration100: AFTER UPDATE).
        cmd.CommandText = $"UPDATE {table} SET local_change_seq = local_change_seq WHERE " +
                          string.Join(" AND ", pk.Select((k, i) => $"{k}=@k{i}")) + ";";
        for (int i = 0; i < pk.Count; i++) cmd.AddWithValue($"@k{i}", parcalar[i]);
        return cmd.ExecuteNonQuery() > 0;
    }

    /// <summary>Kaydı listeden gizler (veriye dokunmaz). Aynı satır yeniden reddedilirse gizli kalır.</summary>
    public void Yoksay(string companyId, string anahtar)
    {
        using var conn = _factory.Create();
        if (!DbIntrospect.TableExists(conn, null, "sync_rejections")) return;
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE sync_rejections SET status='ignored' WHERE anahtar=@a AND company_id=@c;";
        cmd.AddWithValue("@a", anahtar);
        cmd.AddWithValue("@c", companyId);
        cmd.ExecuteNonQuery();
    }

    private static (string? Table, string? Key) Ayir(System.Data.Common.DbConnection conn, string companyId, string anahtar)
    {
        if (!DbIntrospect.TableExists(conn, null, "sync_rejections")) return (null, null);
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT table_name, row_key FROM sync_rejections WHERE anahtar=@a AND company_id=@c;";
        cmd.AddWithValue("@a", anahtar);
        cmd.AddWithValue("@c", companyId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (null, null);
        var t = r.GetString(0);
        // Tablo adı SQL'e girer → yalnız bilinen eşitleme tabloları (beyaz liste).
        return BusinessSyncService.Tables.Contains(t) ? (t, r.GetString(1)) : (null, null);
    }
}
