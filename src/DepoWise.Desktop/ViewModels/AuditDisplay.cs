using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// ⭐ FAZ 4.3 (kullanıcı isteği 2026-09-06) — LOG GÖSTERİM MODELİ.
///
/// Kullanıcı: <i>"…günlere ayırıp bugün bunu yapmış ertesi gün şunu yapmış şeklinde kayıta ait hangi
/// alanda neyi güncelledi ise görebilmeliyim."</i> Bu sınıflar ham log satırlarını tam olarak bu
/// biçime çevirir: GÜN başlığı → o günün işlemleri → her işlemin ALAN BAZLI değişiklikleri.
/// </summary>
public sealed class LogGunu
{
    public string Gun { get; init; } = "";
    public string Ozet { get; init; } = "";
    public List<LogSatiri> Satirlar { get; init; } = new();
}

public sealed class LogSatiri
{
    public string Saat { get; init; } = "";
    public string Islem { get; init; } = "";
    public string Kayit { get; init; } = "";
    public string Kullanici { get; init; } = "";
    public List<string> Degisiklikler { get; init; } = new();
    public bool DegisiklikVar => Degisiklikler.Count > 0;
    public string Not { get; init; } = "";
    public bool NotVar => Not.Length > 0;

    public string EntityType { get; init; } = "";
    public string EntityId { get; init; } = "";
    public ICommand? KayitGecmisiCommand { get; init; }
    /// <summary>"Bu kaydın geçmişi" bağlantısı gösterilsin mi (kayıt ekranında zaten oradayız).</summary>
    public bool KayitBaglantisiVar { get; init; }
}

public static class AuditDisplayBuilder
{
    /// <summary>Log satırlarını GÜNE göre gruplar (en yeni gün üstte) ve her satırın alan bazlı
    /// değişikliklerini okunur metne çevirir.</summary>
    public static List<LogGunu> Gunlere(IReadOnlyList<AuditLogRow> satirlar,
        ICommand? kayitGecmisi = null, bool kayitBaglantisi = false)
    {
        var gunler = new List<LogGunu>();
        foreach (var grup in satirlar.GroupBy(x => x.DayText))
        {
            var gun = new LogGunu
            {
                Gun = GunBasligi(grup.Key),
                Ozet = grup.Count() == 1 ? "1 işlem" : $"{grup.Count()} işlem",
            };
            foreach (var r in grup)
            {
                var degisiklikler = r.Changes.Select(c => "• " + c.Text).ToList();
                gun.Satirlar.Add(new LogSatiri
                {
                    Saat = r.TimeText,
                    Islem = r.ActionText,
                    Kayit = r.EntityLabel,
                    Kullanici = r.UserText,
                    Degisiklikler = degisiklikler,
                    Not = degisiklikler.Count > 0 ? ""
                        : r.BeforeUnknown ? "Bu işlemden önceki hâl kayıtlı değil (alan farkı gösterilemiyor)."
                        : "Bu işlemde izlenen alanlarda değişiklik olmadı.",
                    EntityType = r.EntityType,
                    EntityId = r.EntityId,
                    KayitGecmisiCommand = kayitGecmisi,
                    KayitBaglantisiVar = kayitBaglantisi && !string.IsNullOrWhiteSpace(r.EntityId),
                });
            }
            gunler.Add(gun);
        }
        return gunler;
    }

    /// <summary>"06.09.2026" → "Bugün · 06.09.2026" gibi okunur gün başlığı.</summary>
    private static string GunBasligi(string gun)
    {
        if (!DateTime.TryParseExact(gun, "dd.MM.yyyy", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var t)) return gun;
        var bugun = DateTime.Today;
        if (t.Date == bugun) return "Bugün · " + gun;
        if (t.Date == bugun.AddDays(-1)) return "Dün · " + gun;
        return GunAdi(t.DayOfWeek) + " · " + gun;
    }

    private static string GunAdi(DayOfWeek d) => d switch
    {
        DayOfWeek.Monday => "Pazartesi", DayOfWeek.Tuesday => "Salı", DayOfWeek.Wednesday => "Çarşamba",
        DayOfWeek.Thursday => "Perşembe", DayOfWeek.Friday => "Cuma", DayOfWeek.Saturday => "Cumartesi",
        _ => "Pazar",
    };
}
