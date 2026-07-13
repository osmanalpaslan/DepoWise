using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace DepoWise.Desktop;

/// <summary>
/// FİRMA SENKRONU — OFFLINE-FIRST + KUYRUK (outbox).
///
/// Kural: kullanıcı ÇEVRİMDIŞIYKEN de firma ekleyip/silebilir. İşlem ÖNCE YEREL DB'ye yazılır, sonra
/// <c>sync_outbox</c>'a kuyruklanır. İnternet gelince kuyruk SIRAYLA (FIFO, oluşturulma sırasına göre)
/// sunucuya işlenir. Sunucu tarafı İDEMPOTENT'tir (aynı id/işlem tekrar gelirse hata vermez), böylece
/// yeniden denemelerde kayıt hataya düşmez.
///
/// SIRA (kullanıcının şartı — önce hataya düşürebilecek TANIMLAR, sonra kayıtlar):
///   1) FİRMA kuyruğu (bu servis)   → firma her şeyin ebeveyni; olmadan diğer kayıtlar FK/tenant hatası verir
///   2) tanım/lookup senkronu       → LookupSyncService
///   3) iş verisi push/pull         → BusinessSyncService.Tables zaten FK-güvenli sırada
///      (önce units/suppliers/brands/kategoriler…, sonra personel/malzeme/araç/stok…)
/// Bu sıra <see cref="FlushThenSyncAsync"/> ve login akışında uygulanır.
/// </summary>
public static class CompanySyncService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private const string Entity = "company";

    // ── Yerel + kuyruk (çevrimdışı çalışır) ───────────────────────────────────────────────

    /// <summary>Firma oluştur: YERELE yaz + kuyruğa al. Çevrimiçiyse kuyruk hemen işlenir.</summary>
    public static async Task CreateAsync(DepoWise.Infrastructure.Organization.NewCompany dto)
    {
        var session = DesktopServices.Session ?? throw new InvalidOperationException("Oturum yok.");
        var id = DesktopServices.Companies.Create(session, dto);          // yerel (offline çalışır)
        Enqueue("create", id, Payload(dto, id));
        await TryFlushAsync();
    }

    /// <summary>Firma güncelle: YERELE yaz + kuyruğa al.</summary>
    public static async Task UpdateAsync(string id, DepoWise.Infrastructure.Organization.NewCompany dto)
    {
        var session = DesktopServices.Session ?? throw new InvalidOperationException("Oturum yok.");
        DesktopServices.Companies.Update(session, id, dto);
        Enqueue("update", id, Payload(dto, id));
        await TryFlushAsync();
    }

    /// <summary>Firma sil: YERELE yaz + kuyruğa al.</summary>
    public static async Task DeleteAsync(string id)
    {
        var session = DesktopServices.Session ?? throw new InvalidOperationException("Oturum yok.");
        DesktopServices.Companies.Delete(session, id);
        Enqueue("delete", id, "{}");
        await TryFlushAsync();
    }

    /// <summary>Firmayı yeniden aktifleştir: YERELE yaz + kuyruğa al.</summary>
    public static async Task ReactivateAsync(string id)
    {
        var session = DesktopServices.Session ?? throw new InvalidOperationException("Oturum yok.");
        DesktopServices.Companies.Reactivate(session, id);
        Enqueue("reactivate", id, "{}");
        await TryFlushAsync();
    }

    private static string Payload(DepoWise.Infrastructure.Organization.NewCompany d, string id)
        => JsonSerializer.Serialize(new
        {
            id,
            name = d.Name, taxNo = d.TaxNo, taxOffice = d.TaxOffice, address = d.Address,
            phone = d.Phone, email = d.Email, authorizedPerson = d.AuthorizedPerson, maxUsers = d.MaxUsers,
            maxAdmins = d.MaxAdmins, machineQuota = d.MachineQuota,
        });

    /// <summary>İşlemi outbox'a yazar (operation_id benzersiz → tekrar gönderim güvenli).</summary>
    private static void Enqueue(string op, string companyId, string payloadJson)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var tx = conn.BeginTransaction();
            DepoWise.Infrastructure.Sync.OutboxWriter.Enqueue(
                conn, tx,
                companyId: companyId,
                operationId: Guid.NewGuid().ToString("N"),
                entityType: Entity + ":" + op,          // ör. "company:create"
                entityId: companyId,
                payloadJson: payloadJson,
                baseVersion: null,
                deviceId: Environment.MachineName,
                createdAt: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            tx.Commit();
        }
        catch { /* kuyruk yazılamazsa yerel kayıt yine durur; bir sonraki işlemde tekrar denenir */ }
    }

    /// <summary>Bekleyen firma işlemi sayısı (ekranda "N işlem eşitlenmeyi bekliyor" göstermek için).</summary>
    public static int PendingCount()
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM sync_outbox WHERE status='pending' AND entity_type LIKE 'company:%';";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
        catch { return 0; }
    }

    // ── Kuyruğu sunucuya işleme (internet gelince) ────────────────────────────────────────

    private sealed record PendingOp(string Id, string Op, string EntityId, string Payload);

    /// <summary>Kuyruğu SIRAYLA (FIFO) sunucuya işler. Çevrimdışıysa sessizce çıkar (kayıt kuyrukta kalır).
    /// Bir işlem kalıcı hata verirse 'failed' işaretlenir ve SONRAKİLER İŞLENMEYE DEVAM ETMEZ — sıra bozulmasın
    /// (aynı firmanın create'i geçmeden update'i gitmemeli).</summary>
    public static async Task TryFlushAsync()
    {
        string baseUrl;
        try
        {
            await ServerAuthClient.EnsureFreshTokenAsync();
            if (string.IsNullOrWhiteSpace(ServerAuthClient.BaseUrl) || string.IsNullOrWhiteSpace(ServerAuthClient.Token))
                return;                                   // çevrimdışı → kuyrukta bekle
            baseUrl = ServerAuthClient.BaseUrl!.TrimEnd('/');
        }
        catch { return; }

        foreach (var op in LoadPending())
        {
            bool ok;
            try { ok = await SendAsync(baseUrl, op); }
            catch { return; }                             // ağ koptu → kuyrukta kalsın, sonra devam
            if (!ok) { MarkFailed(op.Id); return; }       // kalıcı hata → sırayı bozmamak için dur
            MarkSent(op.Id);
        }
    }

    /// <summary>Firma kuyruğunu işle, sonra sunucu listesini yerele aynala. (Login/ekran açılışı akışı.)</summary>
    public static async Task FlushThenSyncAsync()
    {
        await TryFlushAsync();      // 1) önce YEREL değişiklikler sunucuya (firma = ebeveyn tanım)
        await MirrorLocalAsync();   // 2) sonra sunucunun gerçeği yerele
    }

    private static List<PendingOp> LoadPending()
    {
        var list = new List<PendingOp>();
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            // FIFO: oluşturulma sırası. Aynı firmanın create → update → delete sırası korunur.
            cmd.CommandText =
                "SELECT id, entity_type, entity_id, payload_json FROM sync_outbox " +
                "WHERE status='pending' AND entity_type LIKE 'company:%' ORDER BY created_at, rowid;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var et = r.GetString(1);
                var op = et.Contains(':') ? et[(et.IndexOf(':') + 1)..] : et;
                list.Add(new PendingOp(r.GetString(0), op, r.GetString(2), r.GetString(3)));
            }
        }
        catch { }
        return list;
    }

    /// <summary>true = uygulandı; false = KALICI hata (4xx). Ağ hatasında exception fırlar (kuyrukta kalır).</summary>
    private static async Task<bool> SendAsync(string baseUrl, PendingOp op)
    {
        HttpRequestMessage req = op.Op switch
        {
            "create"     => Json(HttpMethod.Post, $"{baseUrl}/api/companies", op.Payload),
            "update"     => Json(HttpMethod.Put, $"{baseUrl}/api/companies/{op.EntityId}", op.Payload),
            "delete"     => Auth(new HttpRequestMessage(HttpMethod.Delete, $"{baseUrl}/api/companies/{op.EntityId}")),
            "reactivate" => Json(HttpMethod.Post, $"{baseUrl}/api/companies/{op.EntityId}/reactivate", "{}"),
            _            => throw new InvalidOperationException("Bilinmeyen işlem: " + op.Op),
        };
        using var resp = await _http.SendAsync(req);
        if (resp.IsSuccessStatusCode) return true;
        // 5xx → geçici say, tekrar denenebilsin diye exception (kuyrukta kalır)
        if ((int)resp.StatusCode >= 500) throw new HttpRequestException("Sunucu hatası " + (int)resp.StatusCode);
        return false;   // 4xx → kalıcı hata
    }

    private static HttpRequestMessage Json(HttpMethod m, string url, string json)
    {
        var req = Auth(new HttpRequestMessage(m, url));
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return req;
    }

    private static HttpRequestMessage Auth(HttpRequestMessage req)
    {
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ServerAuthClient.Token);
        return req;
    }

    private static void SetStatus(string id, string status)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE sync_outbox SET status=$s WHERE id=$id;";
            cmd.Parameters.AddWithValue("$s", status);
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        catch { }
    }

    private static void MarkSent(string id) => SetStatus(id, "sent");
    private static void MarkFailed(string id) => SetStatus(id, "failed");

    // ── Sunucu → yerel aynalama ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sunucudaki firma listesini yerele aynalar: gelenler upsert, sunucuda ARTIK OLMAYANLAR pasife alınır.
    /// ÖNEMLİ: kuyrukta BEKLEYEN işlem varsa aynalama YAPILMAZ — yoksa henüz gönderilmemiş yerel firma
    /// "sunucuda yok" sanılıp silinir (veri kaybı). Önce kuyruk boşalır, sonra aynalanır.
    /// Çevrimdışıysa hiçbir şey yapılmaz.
    /// </summary>
    public static async Task MirrorLocalAsync()
    {
        if (PendingCount() > 0) return;    // güvenlik: kuyruk boşalmadan aynalama yok

        string baseUrl;
        try
        {
            await ServerAuthClient.EnsureFreshTokenAsync();
            if (string.IsNullOrWhiteSpace(ServerAuthClient.BaseUrl) || string.IsNullOrWhiteSpace(ServerAuthClient.Token)) return;
            baseUrl = ServerAuthClient.BaseUrl!.TrimEnd('/');
        }
        catch { return; }

        var rows = new List<(string Id, string Name)>();
        try
        {
            using var resp = await _http.SendAsync(Auth(new HttpRequestMessage(HttpMethod.Get, baseUrl + "/api/companies")));
            if (!resp.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                var id = Str(el, "id"); var name = Str(el, "name");
                if (!string.IsNullOrEmpty(id)) rows.Add((id, string.IsNullOrEmpty(name) ? id : name));
            }
        }
        catch { return; }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            using var conn = DesktopServices.Factory.Create();
            foreach (var (id, name) in rows)
            {
                using var c = conn.CreateCommand();
                c.CommandText =
                    "INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES($id,$n,$now,$now,1,0) " +
                    "ON CONFLICT(id) DO UPDATE SET name=$n, is_deleted=0, updated_at=$now;";
                c.Parameters.AddWithValue("$id", id);
                c.Parameters.AddWithValue("$n", name);
                c.Parameters.AddWithValue("$now", now);
                c.ExecuteNonQuery();
            }

            using (var del = conn.CreateCommand())
            {
                var names = new List<string>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var p = "$k" + i;
                    names.Add(p);
                    del.Parameters.AddWithValue(p, rows[i].Id);
                }
                del.CommandText =
                    "UPDATE companies SET is_deleted=1, updated_at=$now WHERE is_deleted=0" +
                    (names.Count > 0 ? " AND id NOT IN (" + string.Join(",", names) + ")" : "") + ";";
                del.Parameters.AddWithValue("$now", now);
                del.ExecuteNonQuery();
            }
        }
        catch { }
    }

    private static string Str(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? "" : "";
}
