using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Security;

/// <summary>
/// "Özel kod" — Kalıcı Silme (Firma Verisi Silme) ekranının kilidi (ADR-083).
///
/// Kurallar (kullanıcı kararı 2026-07-16):
/// - YALNIZ süper adminde vardır ve yalnız ondan istenir. Diğer rollerin giriş akışı hiç değişmez.
/// - İlk web girişinde oluşturulur; şifre gibi HASH'lenir (düz metin asla saklanmaz/dönmez).
/// - Unutulursa süper admin ŞİFRESİYLE yeniden belirlenir (ekran kalıcı kilitlenmesin).
/// - Şifreden AYRI bir sırdır: Kalıcı Silme için şifre + özel kod BİRLİKTE istenir (çift katman).
/// </summary>
public sealed class SpecialCodeService
{
    /// <summary>Kısa/tahmin edilebilir kod geri-alınamaz silmeyi korumaz.</summary>
    public const int MinLength = 6;

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public SpecialCodeService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Bu kullanıcının özel kodu var mı? (yoksa web girişinde oluşturması istenir)</summary>
    public bool HasCode(string userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT special_code_hash FROM users WHERE id=@u AND is_deleted=0;";
        cmd.AddWithValue("@u", userId);
        return !string.IsNullOrEmpty(cmd.ExecuteScalar() as string);
    }

    /// <summary>Süper admin ilk kez özel kod belirler ya da (şifresini doğrulayarak) yenisini yazar.
    /// Şifre doğrulaması ÇAĞIRAN sınır katmanında yapılır (API) — bu metot yalnız yazar.</summary>
    public void SetCode(SessionContext actor, string code)
    {
        if (!actor.IsSuperAdmin) throw new ForbiddenException("Özel kod yalnız süper adminde bulunur.");
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length < MinLength)
            throw new ArgumentException($"Özel kod en az {MinLength} karakter olmalı.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET special_code_hash=@h, updated_at=@now WHERE id=@u AND is_deleted=0;";
            cmd.AddWithValue("@h", PasswordHasher.Hash(code.Trim()));
            cmd.AddWithValue("@now", now);
            cmd.AddWithValue("@u", actor.UserId);
            cmd.ExecuteNonQuery();
        }
        // Audit: özel kodun DEĞERİ değil, değiştirildiği gerçeği kaydedilir.
        AuditWriter.Write(conn, tx, new AuditEntry(actor.CompanyId, "user", actor.UserId, AuditActions.Update, actor.UserId), _clock);
        tx.Commit();
    }

    /// <summary>Özel kodu doğrular. Kod yoksa DAİMA false (fail-closed) — kodsuz ekran açılmaz.</summary>
    public bool Verify(SessionContext actor, string code)
    {
        if (!actor.IsSuperAdmin || string.IsNullOrWhiteSpace(code)) return false;
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT special_code_hash FROM users WHERE id=@u AND is_active=1 AND is_deleted=0;";
        cmd.AddWithValue("@u", actor.UserId);
        var hash = cmd.ExecuteScalar() as string;
        return !string.IsNullOrEmpty(hash) && PasswordHasher.Verify(code.Trim(), hash);
    }
}
