using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Security;

/// <summary>
/// ═══ M-01 (K-20, 2026-10-27) — SÜPER ADMİN İKİ ADIMLI GİRİŞ (doğrulama uygulaması / TOTP) ═══
///
/// <b>Neden:</b> süper admin tüm firmaları yönetir ve sürüm yayınlar; şifresi tek başına yeterliyse şifrenin sızması her şeyi açar.
/// Kod açıldıktan sonra sunucu jeton veren her girişte (web, masaüstü çevrimiçi giriş) şifreye EK olarak 6 haneli kodu ister.
///
/// <b>Kurallar:</b>
/// • Yalnız süper admin rolleri kurar (kullanıcı kararı 2026-10-27: "süper admin için").
/// • Kurulum iki adımlıdır: gizli bilgi üretilir (henüz etkin DEĞİL) → kullanıcı uygulamaya ekleyip ilk kodu girince etkinleşir.
///   Yarım kalan kurulum kimseyi kilitlemez.
/// • Gizli bilgi veritabanında sunucu anahtarıyla ŞİFRELİ (AES-GCM) durur; anahtar ortam değişkeninde (<c>DEPOWISE_MFA_ANAHTARI</c>).
///   Veritabanı sızsa da kodlar üretilemez. Tablo eşitlenmez — masaüstlerine inmez.
/// • Aynı kod iki kez kullanılamaz (son kullanılan adım saklanır; koşullu UPDATE eşzamanlı iki isteği de ayırır).
/// • Telefon kaybına karşı 8 tek kullanımlık KURTARMA KODU verilir (yalnız özetleri saklanır, bir kez gösterilir).
/// </summary>
public sealed class IkiAdimliGirisService
{
    public const string Yayinci = "Alpnex";
    public const int KurtarmaKoduSayisi = 8;

    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly byte[]? _anahtar;

    /// <param name="anahtar">32 baytlık şifreleme anahtarı. Null ise kurulum yapılamaz ve kodlu giriş doğrulanamaz (fail-closed).</param>
    public IkiAdimliGirisService(IDbConnectionFactory factory, byte[]? anahtar, IClock? clock = null)
    {
        if (anahtar is not null && anahtar.Length != 32) throw new ArgumentException("İki adımlı giriş anahtarı 32 bayt olmalı.");
        _factory = factory;
        _anahtar = anahtar;
        _clock = clock ?? new SystemClock();
    }

    /// <summary>Ortam değişkeni değerini (base64, 32 bayt) çözer; yoksa/bozuksa null.</summary>
    public static byte[]? AnahtarCoz(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return null;
        try { var b = Convert.FromBase64String(base64.Trim()); return b.Length == 32 ? b : null; }
        catch (FormatException) { return null; }
    }

    public static bool KurabilirMi(SessionContext s) => s.IsSuperAdmin || s.IsRestrictedSuperAdmin;

    /// <summary>Sunucuda şifreleme anahtarı tanımlı mı? (yoksa web kurulum adımını hiç göstermez)</summary>
    public bool AnahtarVar => _anahtar is not null;

    public sealed record KurulumBilgisi(string GizliBase32, string OtpAuthAdresi, string QrPngBase64);
    public sealed record Durum(bool Etkin, int KalanKurtarmaKodu);

    private sealed record Kayit(string GizliSifreli, long? EtkinlesmeAni, long SonAdim, string? KurtarmaOzetleri);

    /// <summary>Kullanıcının iki adımlı girişi ETKİN mi (kurulumu tamamlanmış mı)?</summary>
    public bool Etkin(string userId) => Oku(userId)?.EtkinlesmeAni is not null;

    public Durum DurumOku(string userId)
    {
        var k = Oku(userId);
        return new Durum(k?.EtkinlesmeAni is not null, k?.EtkinlesmeAni is null ? 0 : Ozetler(k.KurtarmaOzetleri).Count);
    }

    /// <summary>Kurulumu başlatır: yeni gizli bilgi (etkin DEĞİL). Zaten etkinse hata (önce kapatılmalı).</summary>
    public KurulumBilgisi KurulumBaslat(SessionContext s, string hesapAdi)
    {
        if (!KurabilirMi(s)) throw new ForbiddenException("İki adımlı giriş yalnız süper admin hesaplarında kurulur.");
        var anahtar = AnahtarGerekli();
        if (Etkin(s.UserId)) throw new InvalidOperationException("İki adımlı giriş zaten açık.");
        var gizli = RandomNumberGenerator.GetBytes(20);   // 160 bit (RFC 4226 önerisi)
        var simdi = _clock.UtcNow.ToUnixTimeMilliseconds();
        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            Exec(conn, tx, "DELETE FROM user_mfa WHERE user_id=@u;", ("@u", s.UserId));
            Exec(conn, tx, "INSERT INTO user_mfa(user_id, secret_enc, enabled_at, last_step, recovery_hashes, created_at, updated_at) " +
                           "VALUES(@u, @g, NULL, 0, NULL, @t, @t);",
                ("@u", s.UserId), ("@g", Sifrele(anahtar, gizli)), ("@t", simdi));
            tx.Commit();
        }
        var b32 = Totp.Base32(gizli);
        var adres = Totp.OtpAuthAdresi(Yayinci, hesapAdi, b32);
        return new KurulumBilgisi(b32, adres, Convert.ToBase64String(DepoWise.Infrastructure.Reporting.QrLabelService.Png(adres, 6)));
    }

    /// <summary>İlk kodu doğrulayıp etkinleştirir; kurtarma kodlarını (düz metin, BİR KEZ) döndürür.</summary>
    public IReadOnlyList<string> KurulumOnayla(SessionContext s, string? kod)
    {
        if (!KurabilirMi(s)) throw new ForbiddenException("İki adımlı giriş yalnız süper admin hesaplarında kurulur.");
        var anahtar = AnahtarGerekli();
        var k = Oku(s.UserId) ?? throw new InvalidOperationException("Önce kurulumu başlatın.");
        if (k.EtkinlesmeAni is not null) throw new InvalidOperationException("İki adımlı giriş zaten açık.");
        byte[] gizli;
        try { gizli = Coz(anahtar, k.GizliSifreli); }
        catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException("Kurulum bilgisi okunamadı (sunucu anahtarı değişmiş olabilir). Kurulumu yeniden başlatın.");
        }
        var adim = Totp.Dogrula(gizli, kod, _clock.UtcNow, 0)
                   ?? throw new ArgumentException("Doğrulama kodu hatalı. Uygulamadaki güncel 6 haneli kodu girin.");
        var kodlar = Enumerable.Range(0, KurtarmaKoduSayisi).Select(_ => YeniKurtarmaKodu()).ToList();
        var simdi = _clock.UtcNow.ToUnixTimeMilliseconds();
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Exec(conn, tx, "UPDATE user_mfa SET enabled_at=@t, last_step=@a, recovery_hashes=@r, updated_at=@t WHERE user_id=@u;",
            ("@t", simdi), ("@a", adim), ("@r", JsonSerializer.Serialize(kodlar.Select(Ozet).ToList())), ("@u", s.UserId));
        // Açılmadan ÖNCE alınmış (belki çalınmış) jetonlar artık geçmez — yenilemeyle sonsuza dek yaşamasınlar (denetim 2026-10-27).
        OturumDonemi.Arttir(conn, tx, s.UserId);
        // Denetim: gizli bilgi/kod DEĞİL, açıldığı gerçeği yazılır.
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "user_mfa", s.UserId, AuditActions.Create, s.UserId), _clock);
        tx.Commit();
        OturumDonemi.Unut(s.UserId);
        return kodlar;
    }

    /// <summary>Girişte kodu doğrular: 6 haneli uygulama kodu ya da (tek kullanımlık) kurtarma kodu. Etkin değilse false.
    /// Başarılı kod "kullanıldı" olarak işaretlenir — aynı kod ikinci kez kabul edilmez.</summary>
    public bool GirisDogrula(string userId, string? kod)
    {
        if (string.IsNullOrWhiteSpace(kod)) return false;
        var k = Oku(userId);
        if (k?.EtkinlesmeAni is null) return false;
        var temiz = Totp.Temizle(kod);
        if (temiz.Length == Totp.Hane && temiz.All(char.IsAsciiDigit))
        {
            // Uygulama kodu sunucu anahtarı ister; KURTARMA KODU istemez (anahtar kaybolsa da süper admin kurtarma koduyla girer).
            if (_anahtar is null) return false;
            byte[] gizli;
            try { gizli = Coz(_anahtar, k.GizliSifreli); }
            catch (Exception ex) when (ex is CryptographicException or FormatException or ArgumentException) { return false; }   // fail-closed
            var adim = Totp.Dogrula(gizli, temiz, _clock.UtcNow, k.SonAdim);
            if (adim is null) return false;
            using var conn = _factory.Create();
            using var cmd = conn.CreateCommand();
            // Koşullu: aynı kodla eşzamanlı ikinci istek 0 satır günceller → reddedilir.
            cmd.CommandText = "UPDATE user_mfa SET last_step=@a, updated_at=@t WHERE user_id=@u AND last_step < @a;";
            cmd.AddWithValue("@a", adim.Value);
            cmd.AddWithValue("@t", _clock.UtcNow.ToUnixTimeMilliseconds());
            cmd.AddWithValue("@u", userId);
            return cmd.ExecuteNonQuery() == 1;
        }
        var ozetler = Ozetler(k.KurtarmaOzetleri);
        var ozet = Ozet(temiz);
        if (!ozetler.Contains(ozet)) return false;
        ozetler.Remove(ozet);
        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            // Koşullu: liste okunduğundan beri değişmediyse yaz (aynı kurtarma kodu iki kez kullanılamaz).
            cmd.CommandText = "UPDATE user_mfa SET recovery_hashes=@yeni, updated_at=@t WHERE user_id=@u AND recovery_hashes=@eski;";
            cmd.AddWithValue("@yeni", JsonSerializer.Serialize(ozetler));
            cmd.AddWithValue("@t", _clock.UtcNow.ToUnixTimeMilliseconds());
            cmd.AddWithValue("@u", userId);
            cmd.AddWithValue("@eski", k.KurtarmaOzetleri!);
            if (cmd.ExecuteNonQuery() != 1) return false;
            AuditWriter.Write(conn, tx, new AuditEntry("__global__", "user_mfa_recovery", userId, AuditActions.Update, userId), _clock);
            tx.Commit();
        }
        return true;
    }

    /// <summary>Kendi iki adımlı girişini kapatır — geçerli bir kod (uygulama ya da kurtarma) ister.</summary>
    public void Kapat(SessionContext s, string? kod)
    {
        if (!Etkin(s.UserId)) throw new InvalidOperationException("İki adımlı giriş zaten kapalı.");
        if (!GirisDogrula(s.UserId, kod)) throw new ArgumentException("Doğrulama kodu hatalı.");
        using var conn = _factory.Create();
        using var tx = conn.BeginTransaction();
        Exec(conn, tx, "DELETE FROM user_mfa WHERE user_id=@u;", ("@u", s.UserId));
        OturumDonemi.Arttir(conn, tx, s.UserId);   // kapatma da güvenlik değişikliğidir: açık oturumlar yeniden giriş ister
        AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "user_mfa", s.UserId, AuditActions.Delete, s.UserId), _clock);
        tx.Commit();
        OturumDonemi.Unut(s.UserId);
    }

    // ── iç yardımcılar ──────────────────────────────────────────────────────────────────────────────

    private byte[] AnahtarGerekli() => _anahtar ?? throw new InvalidOperationException(
        "Sunucuda iki adımlı giriş anahtarı tanımlı değil (DEPOWISE_MFA_ANAHTARI). Yönetici tanımlamadan kurulum yapılamaz.");

    private Kayit? Oku(string userId)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT secret_enc, enabled_at, last_step, recovery_hashes FROM user_mfa WHERE user_id=@u;";
        cmd.AddWithValue("@u", userId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Kayit(r.GetString(0), r.IsDBNull(1) ? null : Convert.ToInt64(r.GetValue(1)), Convert.ToInt64(r.GetValue(2)),
            r.IsDBNull(3) ? null : r.GetString(3));
    }

    private static List<string> Ozetler(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Kurtarma kodu: 10 karakter (50 bit), okunaklı biçim XXXXX-XXXXX.</summary>
    private static string YeniKurtarmaKodu()
    {
        var b32 = Totp.Base32(RandomNumberGenerator.GetBytes(7))[..10];
        return b32[..5] + "-" + b32[5..];
    }

    private static string Ozet(string kod)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Totp.Temizle(kod).ToUpperInvariant())));

    private static string Sifrele(byte[] anahtar, byte[] acik)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var sifreli = new byte[acik.Length];
        var etiket = new byte[16];
        using var aes = new AesGcm(anahtar, 16);
        aes.Encrypt(nonce, acik, sifreli, etiket);
        return Convert.ToBase64String(nonce.Concat(etiket).Concat(sifreli).ToArray());
    }

    private static byte[] Coz(byte[] anahtar, string paket)
    {
        var b = Convert.FromBase64String(paket);
        var acik = new byte[b.Length - 28];
        using var aes = new AesGcm(anahtar, 16);
        aes.Decrypt(b.AsSpan(0, 12), b.AsSpan(28), b.AsSpan(12, 16), acik);
        return acik;
    }

    private static void Exec(System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx, string sql,
        params (string Ad, object Deger)[] p)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (ad, deger) in p) cmd.AddWithValue(ad, deger);
        cmd.ExecuteNonQuery();
    }
}
