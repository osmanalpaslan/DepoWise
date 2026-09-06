using System;
using System.Collections.Generic;
using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Chat;

/// <summary>Sohbet kişisi: firmadaki bir kullanıcı + çevrimiçi durumu + okunmamış mesaj sayısı.</summary>
/// <param name="Online">Son <see cref="ChatService.CevrimiciSaniye"/> saniye içinde görülmüş mü.</param>
/// <param name="Unread">Bu kişiden gelen ve HENÜZ OKUNMAMIŞ mesaj sayısı.</param>
public sealed record ChatKisi(string UserId, string Username, string? FullName, string? Title,
    bool Online, int Unread, long? LastSeenAt)
{
    /// <summary>Listede gösterilen ad: ad-soyad varsa o, yoksa kullanıcı adı.</summary>
    public string Display => string.IsNullOrWhiteSpace(FullName) ? Username : FullName!;
    public string DurumMetni => Online ? "Çevrim içi" : "Çevrim dışı";
    /// <summary>Baş harf (avatar dairesi için).</summary>
    public string Initial => string.IsNullOrWhiteSpace(Display) ? "?" : Display.Trim()[..1].ToUpperInvariant();
}

/// <summary>Tek mesaj.</summary>
/// <param name="Mine">Bu mesajı ben mi gönderdim (sağa hizalanır).</param>
public sealed record ChatMesaj(string Id, string SenderId, string RecipientId, string Body,
    long CreatedAt, long? ReadAt, bool Mine)
{
    public string SaatMetni => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("HH:mm");
    public string TarihSaatMetni => DateTimeOffset.FromUnixTimeMilliseconds(CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
}

/// <summary>
/// ═══ UYGULAMA İÇİ SOHBET ═══ (kullanıcı isteği 2026-09-06)
///
/// <para><b>Kapsam bilinçli olarak dar:</b> aynı firmanın kullanıcıları arasında BİREBİR mesaj.
/// Grup sohbeti, dosya eki, düzenleme/silme ve bildirim yoktur — istenmedi ve her biri ayrı bir
/// yüzey (ve ayrı bir güvenlik sorusu) açar.</para>
///
/// <para><b>Tenant kapısı her sorguda.</b> Her okuma ve yazma <c>company_id = oturumun firması</c>
/// ile süzülür; alıcının aynı firmada olduğu yazmadan ÖNCE doğrulanır. Şube ayrımı YOKTUR — kullanıcı
/// ofis ile şantiyenin konuşabilmesini istedi; şube bazlı kısıtlama olsaydı asıl amaç ortadan kalkardı.</para>
///
/// <para><b>Çevrimiçi bilgisi.</b> Kullanıcı listesi her istendiğinde çağıranın <c>last_seen_at</c>
/// alanı tazelenir (ayrı bir "heartbeat" ucu yok — yoklamanın kendisi kalp atışıdır). Çevrimiçi
/// sayılmak için son <see cref="CevrimiciSaniye"/> saniye içinde görülmüş olmak gerekir; bu değer
/// yoklama aralığından belirgin biçimde büyüktür ki iki yoklama arasında kimse "kayboldu" görünmesin.</para>
///
/// <para><b>Senkron dışı.</b> <c>chat_messages</c> senkron kataloğunda değildir; masaüstü bu servisi
/// SUNUCU üzerinden kullanır, yerel kopya tutmaz. Çevrimdışıyken sohbet çalışmaz — bilinçli karar
/// (bkz. Migration096).</para>
/// </summary>
public sealed class ChatService
{
    /// <summary>Bu süre içinde görülen kullanıcı "çevrim içi" sayılır.</summary>
    public const int CevrimiciSaniye = 90;

    /// <summary>Tek mesajın azami uzunluğu. Sohbet kutusu bir not defteri değildir; uzun metin
    /// hem arayüzü hem yoklama trafiğini şişirir.</summary>
    public const int AzamiUzunluk = 2000;

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public ChatService(IDbConnectionFactory factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>
    /// Firmadaki kullanıcılar + çevrimiçi durumu + okunmamış sayısı. Çağıranın kendisi listede YER
    /// ALMAZ (kendine mesaj atılmaz). Bu çağrı aynı zamanda çağıranın "görüldü" damgasını tazeler.
    /// </summary>
    public IReadOnlyList<ChatKisi> Kisiler(SessionContext actor)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();

        GorulduDamgala(conn, null, actor.UserId, now);

        using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlDialect.PortableSql(conn, @"
SELECT u.id, u.username, u.full_name, u.title, u.last_seen_at,
  (SELECT COUNT(*) FROM chat_messages m
    WHERE m.company_id = @c AND m.sender_id = u.id AND m.recipient_id = @me
      AND m.read_at IS NULL AND m.is_deleted = 0)
FROM users u
WHERE u.is_deleted = 0 AND u.is_active = 1 AND u.company_id = @c AND u.id <> @me
ORDER BY u.full_name, u.username;");
        cmd.AddWithValue("@c", actor.CompanyId);
        cmd.AddWithValue("@me", actor.UserId);

        var esik = now - (CevrimiciSaniye * 1000L);
        var liste = new List<ChatKisi>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long? gorulme = r.IsDBNull(4) ? null : r.GetInt64(4);
            liste.Add(new ChatKisi(
                r.GetString(0), r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                gorulme is { } g && g >= esik,
                (int)r.GetInt64(5),
                gorulme));
        }
        return liste;
    }

    /// <summary>
    /// İki kişi arasındaki konuşma (en yeni <paramref name="limit"/> mesaj, ESKİDEN YENİYE sıralı).
    /// <paramref name="sinceMs"/> verilirse yalnız o andan SONRAKİ mesajlar döner — yoklama her
    /// seferinde tüm geçmişi taşımaz.
    /// </summary>
    public IReadOnlyList<ChatMesaj> Konusma(SessionContext actor, string karsiUserId, long? sinceMs = null, int limit = 200)
    {
        if (string.IsNullOrWhiteSpace(karsiUserId)) return Array.Empty<ChatMesaj>();
        if (limit is < 1 or > 500) limit = 200;

        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = SqlDialect.PortableSql(conn, @"
SELECT id, sender_id, recipient_id, body, created_at, read_at
FROM chat_messages
WHERE company_id = @c AND is_deleted = 0
  AND ((sender_id = @me AND recipient_id = @o) OR (sender_id = @o AND recipient_id = @me))
  AND (@since IS NULL OR created_at > @since)
ORDER BY created_at DESC
LIMIT @lim;");
        cmd.AddWithValue("@c", actor.CompanyId);
        cmd.AddWithValue("@me", actor.UserId);
        cmd.AddWithValue("@o", karsiUserId);
        cmd.AddWithValue("@since", (object?)sinceMs ?? DBNull.Value);
        cmd.AddWithValue("@lim", limit);

        var ters = new List<ChatMesaj>();
        using (var r = cmd.ExecuteReader())
            while (r.Read())
                ters.Add(new ChatMesaj(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                    r.GetInt64(4), r.IsDBNull(5) ? null : r.GetInt64(5),
                    string.Equals(r.GetString(1), actor.UserId, StringComparison.Ordinal)));

        // Sorgu "en yeni N" için DESC; ekranda eskiden yeniye okunur.
        ters.Reverse();
        return ters;
    }

    /// <summary>
    /// Mesaj gönderir. Alıcı AYNI FİRMADA ve aktif olmalıdır; değilse gönderim reddedilir
    /// (firma sınırı geçirgen olamaz). Boş mesaj gönderilmez, uzunluk sınırı uygulanır.
    /// </summary>
    /// <returns>Oluşan mesajın kimliği.</returns>
    public string Gonder(SessionContext actor, string aliciUserId, string govde)
    {
        govde = (govde ?? "").Trim();
        if (govde.Length == 0) throw new InvalidOperationException("Boş mesaj gönderilemez.");
        if (govde.Length > AzamiUzunluk)
            throw new InvalidOperationException($"Mesaj çok uzun ({govde.Length} karakter). En çok {AzamiUzunluk} karakter olabilir.");
        if (string.Equals(aliciUserId, actor.UserId, StringComparison.Ordinal))
            throw new InvalidOperationException("Kendinize mesaj gönderemezsiniz.");

        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();

        if (!AyniFirmadaAktif(conn, tx, actor.CompanyId, aliciUserId))
            throw new ForbiddenException("Alıcı bu firmada bulunamadı.");

        var id = Guid.NewGuid().ToString("N");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO chat_messages(id, company_id, sender_id, recipient_id, body, created_at, read_at, is_deleted) " +
                              "VALUES(@id,@c,@s,@r,@b,@t,NULL,0);";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", actor.CompanyId);
            cmd.AddWithValue("@s", actor.UserId);
            cmd.AddWithValue("@r", aliciUserId);
            cmd.AddWithValue("@b", govde);
            cmd.AddWithValue("@t", now);
            cmd.ExecuteNonQuery();
        }

        GorulduDamgala(conn, tx, actor.UserId, now);
        tx.Commit();
        return id;
    }

    /// <summary>Bir kişiden gelen tüm okunmamış mesajları okundu işaretler.</summary>
    /// <returns>İşaretlenen mesaj sayısı.</returns>
    public int OkunduIsaretle(SessionContext actor, string karsiUserId)
    {
        var now = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE chat_messages SET read_at=@t " +
                          "WHERE company_id=@c AND sender_id=@o AND recipient_id=@me AND read_at IS NULL AND is_deleted=0;";
        cmd.AddWithValue("@t", now);
        cmd.AddWithValue("@c", actor.CompanyId);
        cmd.AddWithValue("@o", karsiUserId);
        cmd.AddWithValue("@me", actor.UserId);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Toplam okunmamış mesaj sayısı (alt bardaki rozet için).</summary>
    public int ToplamOkunmamis(SessionContext actor)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM chat_messages WHERE company_id=@c AND recipient_id=@me AND read_at IS NULL AND is_deleted=0;";
        cmd.AddWithValue("@c", actor.CompanyId);
        cmd.AddWithValue("@me", actor.UserId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    /// <summary>Alıcı aynı firmada ve aktif mi? Firma sınırının yazma tarafındaki kapısı.</summary>
    private static bool AyniFirmadaAktif(DbConnection conn, DbTransaction? tx, string companyId, string userId)
    {
        using var cmd = conn.CreateCommand();
        if (tx is not null) cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM users WHERE id=@u AND company_id=@c AND is_deleted=0 AND is_active=1;";
        cmd.AddWithValue("@u", userId);
        cmd.AddWithValue("@c", companyId);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    /// <summary>"Şu an buradayım" damgası. Ayrı bir uç yoktur: yoklamanın kendisi kalp atışıdır.</summary>
    private static void GorulduDamgala(DbConnection conn, DbTransaction? tx, string userId, long now)
    {
        try
        {
            using var cmd = conn.CreateCommand();
            if (tx is not null) cmd.Transaction = tx;
            cmd.CommandText = "UPDATE users SET last_seen_at=@t WHERE id=@u AND is_deleted=0;";
            cmd.AddWithValue("@t", now);
            cmd.AddWithValue("@u", userId);
            cmd.ExecuteNonQuery();
        }
        catch { /* çevrimiçi göstergesi süstür: yazılamazsa sohbet ÇALIŞMAYA DEVAM eder */ }
    }
}
