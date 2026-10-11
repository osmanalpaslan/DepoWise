using System.Collections.Generic;
using System.Linq;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ⭐ 2026-10-11 — GÜNCELLEME NOTLARI ekranı (kullanıcı isteği: "neler yaptığımızı takip edeceğimiz bir ekran").
/// Kaynak TEK: <see cref="GuncellemeNotlari"/> (web ile aynı liste). Yalnız masaüstüne ait/ortak maddeler gösterilir.
/// Veri yereldir (uygulamanın içinde) → çevrimdışı da çalışır, sunucu çağrısı yok.
/// </summary>
public sealed partial class UpdateNotesViewModel : ViewModelBase
{
    public sealed record NotKarti(string Baslik, string TarihMetni, string? Surum, IReadOnlyList<string> Maddeler, bool BuSurum)
    {
        public string Ust => Surum is null ? TarihMetni : $"{TarihMetni} · Sürüm {Surum}";
        public bool HasSurum => Surum is not null;
    }

    public IReadOnlyList<NotKarti> Notlar { get; }
    public bool Bos => Notlar.Count == 0;

    public UpdateNotesViewModel(SessionContext session, string? mevcutSurum = null)
    {
        mevcutSurum ??= GuncellemeNotuGosterici.MevcutSurum();
        Notlar = GuncellemeNotlari.PlatformIcin(web: false)
            .Select(n => new NotKarti(n.Baslik, n.TarihMetni, n.MasaustuSurumu,
                n.MaddelerIcin(web: false).Select(m => m.Metin).ToList(),
                n.MasaustuSurumu is not null && n.MasaustuSurumu == mevcutSurum))
            .ToList();
    }
}
