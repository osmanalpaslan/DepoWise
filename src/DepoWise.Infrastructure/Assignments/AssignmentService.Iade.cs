using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Assignments;

/// <summary>⭐ M-46: zimmette olan bir varlığın beklenen iade bilgisi.</summary>
public sealed record ZimmetIadesi(HoldingRow Zimmet, long? BeklenenIade, int? GecikmeGun)
{
    public bool Gecikti => GecikmeGun is > 0;
    public string IadeMetni => BeklenenIade is { } t
        ? DateTimeOffset.FromUnixTimeMilliseconds(t).UtcDateTime.ToString("dd.MM.yyyy") + (Gecikti ? $" · {GecikmeGun} gün gecikti" : "")
        : "—";
    public string Ozet => $"{Zimmet.PersonnelName} · {Zimmet.AssetLabel} ({Zimmet.QuantityDisplay}) · iade: {IadeMetni}";
}

/// <summary>
/// ═══ M-46 (AR-GE 2026-10-19, 05 F-081) — ZİMMETTE BEKLENEN İADE TARİHİ · GECİKME · PERSONEL KARTINDA ZİMMETLER ═══
/// Beklenen iade tarihi defter satırına YAZILMAZ (defter değişmez); ayrı üst bilgidir (<c>assignment_due_dates</c>). Varlık
/// iade/kayıp/devirle kişiden çıkınca beklenti kendiliğinden anlamsızlaşır (yalnız "kimde ne var"daki kalemler listelenir).
/// İnşaatta alet kaybını ve işten ayrılan personelin açık zimmetini hedefler.
/// </summary>
public sealed partial class AssignmentService
{
    private static bool IadeDestekli(System.Data.Common.DbConnection conn) => DbIntrospect.TableExists(conn, null, "assignment_due_dates");

    /// <summary>Beklenen iade tarihini ayarlar (null = kaldırır). Varlık o kişide zimmetli olmalı.</summary>
    public void IadeTarihiAyarla(SessionContext s, string personnelId, string assetType, string assetId, long? tarih)
    {
        AccessControl.Require(s, Module, PermissionAction.Edit);
        if (assetType is not ("material" or "equipment")) throw new ArgumentException("Geçersiz varlık türü.");
        if (Holdings(s, assetType: assetType, personnelId: personnelId).All(h => h.AssetId != assetId))
            throw new ArgumentException("Bu varlık bu personelin zimmetinde değil.");
        using var conn = _factory.Create();
        if (!IadeDestekli(conn)) throw new InvalidOperationException("Veritabanı beklenen iade tarihini henüz desteklemiyor (güncelleme bekleniyor).");
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var tx = conn.BeginTransaction();
        using (var del = conn.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM assignment_due_dates WHERE company_id=@c AND personnel_id=@p AND asset_type=@t AND asset_id=@a;";
            del.AddWithValue("@c", s.CompanyId); del.AddWithValue("@p", personnelId); del.AddWithValue("@t", assetType); del.AddWithValue("@a", assetId);
            del.ExecuteNonQuery();
        }
        if (tarih is { } t)
            using (var ins = conn.CreateCommand())
            {
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO assignment_due_dates(company_id, personnel_id, asset_type, asset_id, expected_return, updated_by, updated_at) " +
                                  "VALUES(@c,@p,@t,@a,@e,@u,@n);";
                ins.AddWithValue("@c", s.CompanyId); ins.AddWithValue("@p", personnelId); ins.AddWithValue("@t", assetType); ins.AddWithValue("@a", assetId);
                ins.AddWithValue("@e", t); ins.AddWithValue("@u", s.UserId); ins.AddWithValue("@n", now);
                ins.ExecuteNonQuery();
            }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "assignment_due", $"{personnelId}:{assetType}:{assetId}", AuditActions.Update, s.UserId,
            AfterJson: tarih is { } tt ? $"{{\"beklenenIade\":{tt}}}" : "{\"beklenenIade\":null}"), _clock);
        tx.Commit();
    }

    /// <summary>Zimmetteki kalemler + beklenen iade + gecikme. <paramref name="personnelId"/> verilirse personel kartı görünümü.</summary>
    public IReadOnlyList<ZimmetIadesi> IadeDurumu(SessionContext s, string? personnelId = null, bool yalnizGeciken = false)
    {
        var zimmetler = Holdings(s, personnelId: personnelId);
        var beklenti = new Dictionary<(string, string, string), long>();
        using (var conn = _factory.Create())
            if (IadeDestekli(conn))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT personnel_id, asset_type, asset_id, expected_return FROM assignment_due_dates WHERE company_id=@c;";
                cmd.AddWithValue("@c", s.CompanyId);
                using var r = cmd.ExecuteReader();
                while (r.Read()) beklenti[(r.GetString(0), r.GetString(1), r.GetString(2))] = Convert.ToInt64(r.GetValue(3));
            }
        var bugun = _clock.UtcNow.ToUnixTimeMilliseconds();
        bugun -= bugun % 86_400_000L;
        var sonuc = zimmetler.Select(z =>
        {
            long? b = beklenti.TryGetValue((z.PersonnelId, z.AssetType, z.AssetId), out var v) ? v : null;
            int? gec = b is { } bb && bb < bugun ? (int)((bugun - bb) / 86_400_000L) : null;
            return new ZimmetIadesi(z, b, gec);
        });
        return (yalnizGeciken ? sonuc.Where(x => x.Gecikti).OrderByDescending(x => x.GecikmeGun) : sonuc).ToList();
    }
}
