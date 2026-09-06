using System.Linq;

namespace DepoWise.Application.Ui;

/// <summary>
/// ═══ İLETİŞİM ALANI DOĞRULAMASI — e-posta ve telefon ═══ (kullanıcı isteği 2026-09-06)
///
/// <para>Kural TEK YERDE durur: sunucu (<c>UserService.UpdateProfile</c>) bunu uygular, masaüstü ve
/// web de aynı işlevi çağırarak kullanıcıya ANINDA geri bildirim verir. Böylece "arayüz kabul etti,
/// sunucu reddetti" çelişkisi oluşmaz. Gerçek kapı yine sunucudadır — istemci doğrulaması yalnız
/// kolaylıktır.</para>
///
/// <para><b>Kasıtlı olarak GEVŞEK.</b> Amaç yazım hatasını yakalamaktır, standartları eksiksiz
/// uygulamak değil. Aşırı katı bir kural geçerli adresleri/numaraları reddederek kullanıcıyı kilitler;
/// bu ekranlarda alanların ikisi de <b>zorunlu değildir</b> — boş bırakmak serbesttir.</para>
/// </summary>
public static class IletisimDogrulama
{
    /// <summary>
    /// E-posta biçimi. Kural: boşluk yok · tam bir tane <c>@</c> · <c>@</c>'dan önce içerik ·
    /// alan adında en az bir nokta · sondaki uzantı en az iki HARF.
    /// </summary>
    public static bool EpostaGecerli(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;   // boş = alan doldurulmadı, hata değil
        s = s.Trim();
        if (s.Any(char.IsWhiteSpace)) return false;

        var parcalar = s.Split('@');
        if (parcalar.Length != 2) return false;
        if (parcalar[0].Length == 0) return false;

        var alan = parcalar[1];
        var nokta = alan.LastIndexOf('.');
        if (nokta <= 0 || nokta == alan.Length - 1) return false;

        var uzanti = alan[(nokta + 1)..];
        return uzanti.Length >= 2 && uzanti.All(char.IsLetter);
    }

    /// <summary>
    /// Telefon. Biçim DAYATILMAZ — "0500 111 22 33", "+90 500 111 22 33", "05001112233" hepsi geçerli.
    /// Yalnız rakam SAYISI bakılır: Türkiye'de cep numarası alan koduyla 10 rakamdır (başında 0 veya
    /// +90 olabilir) → alt sınır 10; üst sınır 15 (ITU E.164 azami uzunluğu).
    /// </summary>
    public static bool TelefonGecerli(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return true;   // boş = alan doldurulmadı, hata değil
        var rakam = s.Count(char.IsDigit);
        if (rakam < 10 || rakam > 15) return false;
        // Rakam ve yaygın ayraçlar dışında bir şey varsa (harf gibi) yazım hatasıdır.
        return s.All(c => char.IsDigit(c) || c is ' ' or '(' or ')' or '-' or '+' or '/' or '.');
    }

    /// <summary>Hatalıysa kullanıcıya gösterilecek Türkçe mesaj; geçerliyse <c>null</c>.</summary>
    public static string? EpostaHatasi(string? s)
        => EpostaGecerli(s) ? null : "E-posta adresi geçersiz görünüyor. Örnek: ad@firma.com";

    /// <summary>Hatalıysa kullanıcıya gösterilecek Türkçe mesaj; geçerliyse <c>null</c>.</summary>
    public static string? TelefonHatasi(string? s)
        => TelefonGecerli(s) ? null : "Telefon numarası geçersiz görünüyor. En az 10 rakam olmalı (ör. 0500 111 22 33).";
}
