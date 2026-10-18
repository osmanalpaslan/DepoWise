using System.Collections.Concurrent;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Security;

/// <summary>
/// ⭐ M-30 (AR-GE 2026-10-18, 05 F-076) — DÖNEM KİLİDİ okuma/ayarlama (<c>companies.period_lock_date</c>, Migration106).
/// Okuma her oturum kurulumunda yapılır → 30 sn NESNEYE ÖZEL önbellek (testlerde aynı firma kimliği farklı veritabanlarında
/// olabilir); ayarlama süreç genelindeki sayacı ilerletir → tüm nesnelerde kayıt anında bayatlar. Kolon yoksa kilit yok.
/// </summary>
public sealed class DonemKilidiService
{
    private static readonly ConcurrentDictionary<string, long> Degisim = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (long? Kilit, long Zaman, long Degisim)> _onbellek = new(StringComparer.Ordinal);
    private const long OnbellekMs = 30_000;
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public DonemKilidiService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public long? Oku(string companyId)
    {
        var simdi = Environment.TickCount64;
        var sayac = Degisim.GetValueOrDefault(companyId);
        if (_onbellek.TryGetValue(companyId, out var o) && simdi - o.Zaman < OnbellekMs && o.Degisim == sayac) return o.Kilit;
        long? kilit = null;
        using (var conn = _factory.Create())
        {
            if (DbIntrospect.ColumnExists(conn, null, "companies", "period_lock_date"))
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT period_lock_date FROM companies WHERE id=@c;";
                cmd.AddWithValue("@c", companyId);
                var v = cmd.ExecuteScalar();
                kilit = v is null or DBNull ? null : Convert.ToInt64(v);
            }
        }
        _onbellek[companyId] = (kilit, simdi, sayac);
        return kilit;
    }

    /// <summary>Kilidi ayarlar ya da kaldırır (null). Yalnız firma yöneticisi / süper admin. Tarih UTC gün başına yuvarlanır;
    /// bugün ve sonrası kilitlenemez (bugünün işleri durmasın). İşlem geçmişine yazılır.</summary>
    public void Ayarla(SessionContext s, long? tarih)
    {
        if (!AccessControl.IsAdmin(s)) throw new ForbiddenException("Dönem kilidini yalnız yönetici ayarlayabilir.");
        long? gun = tarih is { } t ? t - ((t % DonemKilidi.GunMs) + DonemKilidi.GunMs) % DonemKilidi.GunMs : null;
        var bugun = _clock.UtcNow.ToUnixTimeMilliseconds();
        bugun -= bugun % DonemKilidi.GunMs;
        if (gun is { } g && g >= bugun) throw new ArgumentException("Dönem kilidi bugünden önceki bir gün olmalı (bugünün işleri durmasın).");
        using var conn = _factory.Create();
        if (!DbIntrospect.ColumnExists(conn, null, "companies", "period_lock_date"))
            throw new InvalidOperationException("Veritabanı dönem kilidini henüz desteklemiyor (güncelleme bekleniyor).");
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE companies SET period_lock_date=@k WHERE id=@c;";
            cmd.AddWithValue("@k", (object?)gun ?? DBNull.Value);
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.ExecuteNonQuery();
        }
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "company", s.CompanyId, AuditActions.Update, s.UserId,
            AfterJson: gun is { } gg ? $"{{\"donemKilidi\":\"{DonemKilidi.Tarih(gg)}\"}}" : "{\"donemKilidi\":null}"), _clock);
        tx.Commit();
        Unut(s.CompanyId);
    }

    /// <summary>Önbellekteki kaydı tüm nesnelerde bayatlatır (masaüstü giriş paketinden yerel değeri yazınca da çağrılır).</summary>
    public static void Unut(string companyId) => Degisim.AddOrUpdate(companyId, 1, (_, v) => v + 1);
}
