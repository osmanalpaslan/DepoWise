using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DepoWise.Infrastructure.Assignments;

/// <summary>⭐ M-47: dondurulmuş tutanak satırı.</summary>
public sealed record TutanakKalemi(string Tur, string Varlik, string Miktar, string TeslimTarihi, string? BeklenenIade);

/// <summary>⭐ M-47: zimmet kabul tutanağı (oluşturulduğu andaki liste donmuştur).</summary>
public sealed record ZimmetTutanagi(string Id, string No, string PersonnelId, string PersonelAdi, string Durum, long Olusturma,
    long? KararAni, string? KararNotu, IReadOnlyList<TutanakKalemi> Kalemler)
{
    public string DurumMetni => Durum switch { "kabul" => "Kabul edildi", "ret" => "Reddedildi", _ => "İmza bekliyor" };
    public string Ozet => $"{No} · {PersonelAdi} · {DateTimeOffset.FromUnixTimeMilliseconds(Olusturma).UtcDateTime:dd.MM.yyyy} · {Kalemler.Count} kalem · {DurumMetni}";
}

/// <summary>
/// ═══ M-47 (AR-GE 2026-10-19, 05 F-082) — KİŞİ BAZLI İMZALI ZİMMET TUTANAĞI ═══
/// Tutanak oluşturulunca kişinin o anki zimmetleri DONDURULUR (sonraki teslim/iade tutanağı değiştirmez) ve PDF'i
/// imza alanlarıyla basılır. Islak imzalı kâğıt dönünce durum "kabul" (ya da gerekçeli "ret") işaretlenir → "almadım"
/// itirazına kanıt. KVKK: tutanakta yalnız ad ve zimmet bilgisi vardır (T.C. kimlik vb. YOK).
/// </summary>
public sealed class ZimmetTutanakService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly AssignmentService _zimmet;

    static ZimmetTutanakService() { QuestPDF.Settings.License = LicenseType.Community; }

    public ZimmetTutanakService(IDbConnectionFactory factory, AssignmentService zimmet, IClock? clock = null)
    {
        _factory = factory;
        _zimmet = zimmet;
        _clock = clock ?? new SystemClock();
    }

    private static bool Destekli(System.Data.Common.DbConnection conn) => DbIntrospect.TableExists(conn, null, "assignment_receipts");

    /// <summary>Tutanak oluşturur (zimmetler dondurulur). Kişinin zimmeti yoksa reddedilir.</summary>
    public ZimmetTutanagi Olustur(SessionContext s, string personnelId)
    {
        AccessControl.Require(s, AssignmentService.Module, PermissionAction.Create);
        var kalemler = _zimmet.IadeDurumu(s, personnelId);
        if (kalemler.Count == 0) throw new ArgumentException("Bu personelin zimmetinde kalem yok; tutanak oluşturulmaz.");
        var adi = kalemler[0].Zimmet.PersonnelName;
        var snap = kalemler.Select(k => new TutanakKalemi(k.Zimmet.AssetTypeDisplay, k.Zimmet.AssetLabel, k.Zimmet.QuantityDisplay,
            k.Zimmet.LastDocDateDisplay, k.BeklenenIade is { } b ? DateTimeOffset.FromUnixTimeMilliseconds(b).UtcDateTime.ToString("dd.MM.yyyy") : null)).ToList();
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı zimmet tutanağını henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginImmediate();
        var no = BelgeNumarasi.Sonraki(conn, tx, "assignment_receipts", "receipt_no", s.CompanyId,
            $"ZT-{DateTimeOffset.FromUnixTimeMilliseconds(now).Year}-", s.MachineCode);
        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO assignment_receipts(id, company_id, receipt_no, personnel_id, status, snapshot_json, created_by, created_at, updated_at, version, is_deleted) " +
                              "VALUES(@id,@c,@no,@p,'bekliyor',@j,@u,@n,@n,1,0);";
            cmd.AddWithValue("@id", id); cmd.AddWithValue("@c", s.CompanyId); cmd.AddWithValue("@no", no); cmd.AddWithValue("@p", personnelId);
            cmd.AddWithValue("@j", JsonSerializer.Serialize(new { adi, kalemler = snap }));
            cmd.AddWithValue("@u", s.UserId); cmd.AddWithValue("@n", now);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_receipt", id, AuditActions.Create, s.UserId,
            AfterJson: $"{{\"no\":\"{no}\",\"kalem\":{snap.Count}}}"), _clock);
        tx.Commit();
        return new ZimmetTutanagi(id, no, personnelId, adi, "bekliyor", now, null, null, snap);
    }

    /// <summary>Tutanaklar (personel verilirse onunkiler), en yeni üstte.</summary>
    public IReadOnlyList<ZimmetTutanagi> Liste(SessionContext s, string? personnelId = null)
    {
        AccessControl.Require(s, AssignmentService.Module, PermissionAction.View);
        using var conn = _factory.Create();
        if (!Destekli(conn)) return Array.Empty<ZimmetTutanagi>();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, receipt_no, personnel_id, status, snapshot_json, created_at, decided_at, decision_note FROM assignment_receipts " +
                          "WHERE company_id=@c AND is_deleted=0" + (personnelId is null ? "" : " AND personnel_id=@p") + " ORDER BY created_at DESC;";
        cmd.AddWithValue("@c", s.CompanyId);
        if (personnelId is not null) cmd.AddWithValue("@p", personnelId);
        var list = new List<ZimmetTutanagi>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            using var doc = JsonDocument.Parse(r.GetString(4));
            var adi = doc.RootElement.GetProperty("adi").GetString() ?? "";
            var kalemler = doc.RootElement.GetProperty("kalemler").EnumerateArray().Select(k => new TutanakKalemi(
                k.GetProperty("Tur").GetString() ?? "", k.GetProperty("Varlik").GetString() ?? "", k.GetProperty("Miktar").GetString() ?? "",
                k.GetProperty("TeslimTarihi").GetString() ?? "",
                k.TryGetProperty("BeklenenIade", out var b) && b.ValueKind == JsonValueKind.String ? b.GetString() : null)).ToList();
            list.Add(new ZimmetTutanagi(r.GetString(0), r.GetString(1), r.GetString(2), adi, r.GetString(3), Convert.ToInt64(r.GetValue(5)),
                r.IsDBNull(6) ? null : Convert.ToInt64(r.GetValue(6)), r.IsDBNull(7) ? null : r.GetString(7), kalemler));
        }
        return list;
    }

    /// <summary>Islak imzalı kâğıt döndü: kabul (ya da gerekçeli ret). Yalnız "bekliyor" durumundaki tutanakta.</summary>
    public void Karar(SessionContext s, string tutanakId, bool kabul, string? not)
    {
        AccessControl.Require(s, AssignmentService.Module, PermissionAction.Edit);
        if (!kabul && string.IsNullOrWhiteSpace(not)) throw new ArgumentException("Ret gerekçesi zorunlu (ör. \"listede olmayan kalem var\").");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        if (!Destekli(conn)) throw new InvalidOperationException("Veritabanı zimmet tutanağını henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE assignment_receipts SET status=@st, decided_by=@u, decided_at=@n, decision_note=@not, updated_at=@n, version=version+1 " +
                              "WHERE id=@id AND company_id=@c AND is_deleted=0 AND status='bekliyor';";
            cmd.AddWithValue("@st", kabul ? "kabul" : "ret"); cmd.AddWithValue("@u", s.UserId); cmd.AddWithValue("@n", now);
            cmd.AddWithValue("@not", string.IsNullOrWhiteSpace(not) ? DBNull.Value : not!.Trim());
            cmd.AddWithValue("@id", tutanakId); cmd.AddWithValue("@c", s.CompanyId);
            if (cmd.ExecuteNonQuery() == 0) throw new InvalidOperationException("Tutanak bulunamadı ya da kararı zaten verilmiş.");
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_receipt", tutanakId, AuditActions.Update, s.UserId,
            AfterJson: $"{{\"durum\":\"{(kabul ? "kabul" : "ret")}\"}}"), _clock);
        tx.Commit();
    }

    /// <summary>Tutanağın PDF'i (dondurulmuş listeden; imza alanlarıyla).</summary>
    public byte[] Pdf(SessionContext s, string tutanakId)
    {
        var t = Liste(s).FirstOrDefault(x => x.Id == tutanakId) ?? throw new ForbiddenException("Tutanak bulunamadı veya başka firmaya ait.");
        string firmaAdi;
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT name FROM companies WHERE id=@c;";
            cmd.AddWithValue("@c", s.CompanyId);
            firmaAdi = cmd.ExecuteScalar() as string ?? "";
        }
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.6f, Unit.Centimetre);
            page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));
            page.Header().Column(c =>
            {
                c.Item().Text(firmaAdi).FontSize(13).Bold();
                c.Item().Text($"ZİMMET TESLİM TUTANAĞI — {t.No}").FontSize(12).Bold();
                c.Item().Text($"Personel: {t.PersonelAdi}    Tarih: {DateTimeOffset.FromUnixTimeMilliseconds(t.Olusturma).UtcDateTime:dd.MM.yyyy}");
            });
            page.Content().PaddingVertical(10).Column(c =>
            {
                c.Item().Table(tb =>
                {
                    tb.ColumnsDefinition(cd => { cd.ConstantColumn(24); cd.ConstantColumn(64); cd.RelativeColumn(); cd.ConstantColumn(56); cd.ConstantColumn(70); cd.ConstantColumn(70); });
                    tb.Header(h =>
                    {
                        foreach (var b in new[] { "#", "Tür", "Malzeme / Ekipman", "Miktar", "Teslim", "İade" })
                            h.Cell().Background("#1F2A44").Padding(3).Text(b).FontColor("#FFFFFF").Bold();
                    });
                    var i = 0;
                    foreach (var k in t.Kalemler)
                    {
                        i++;
                        var zemin = i % 2 == 0 ? "#EEF3FF" : "#FFFFFF";
                        foreach (var v in new[] { i.ToString(), k.Tur, k.Varlik, k.Miktar, k.TeslimTarihi, k.BeklenenIade ?? "—" })
                            tb.Cell().Background(zemin).Padding(3).Text(v);
                    }
                });
                c.Item().PaddingTop(14).Text("Yukarıda listelenen malzeme ve ekipmanları sağlam ve eksiksiz olarak teslim aldım. " +
                    "Bunları özenle kullanacağımı, kaybolması ya da hasar görmesi durumunda derhal bildireceğimi ve istendiğinde " +
                    "(ya da işten ayrılırken) eksiksiz iade edeceğimi kabul ederim.");
                c.Item().PaddingTop(36).Row(r =>
                {
                    r.RelativeItem().Column(x => { x.Item().Text("TESLİM EDEN").Bold(); x.Item().PaddingTop(28).Text("Ad Soyad / İmza: ____________________"); });
                    r.RelativeItem().Column(x => { x.Item().Text("TESLİM ALAN").Bold(); x.Item().Text(t.PersonelAdi); x.Item().PaddingTop(14).Text("İmza: ____________________"); });
                });
            });
            page.Footer().AlignCenter().Text($"{t.No} · {t.DurumMetni} · Bu tutanak oluşturulduğu andaki zimmet listesini gösterir.").FontSize(8);
        })).GeneratePdf();
    }
}
