using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Desktop.ViewModels;

namespace DepoWise.Desktop.Views;

/// <summary>
/// ⭐ FAZ 4.4 (kullanıcı isteği 2026-09-06) — <b>SENKRON ÇAKIŞMA EKRANI (masaüstü).</b>
///
/// <i>"Senkron çakışma uyarısı web'te var masaüstünde de olmalı. Kimin kazandığı kimin kaybettiği
/// belirtilmeli. Uyarıya tıklandığında yeni bir senkron çakışma ekranı açılmalı. Üzerine yazılan
/// kaydı iptal edip istenen kaydı kazanan yapabilmeli."</i>
///
/// <b>Veri kaynağı SUNUCUDUR.</b> Çakışmalar <c>data_conflicts</c> tablosunda sunucuda tutulur;
/// bu pencere HTTP ile okur ve çözüm isteğini sunucuya gönderir. Yerel kopya tutulmaz — çevrimdışıyken
/// eski bilgiyle karar verilmesi engellenir (kullanıcıya açıkça "bağlı değilsiniz" denir).
///
/// <b>Yetki.</b> Listeyi görmek yetki gerektirmez (uyarı zaten herkese gösteriliyor); <b>kazananı
/// değiştirmek</b> <see cref="SpecialButtons.ConflictResolve"/> yetkisine bağlıdır ve asıl kapı
/// SUNUCUDADIR. Buradaki görünürlük yalnız arayüz kolaylığıdır.
/// </summary>
public partial class SyncConflictsWindow : Window
{
    private SessionContext? _session;

    public SyncConflictsWindow() => AvaloniaXamlLoader.Load(this);

    private SyncConflictsWindow(SessionContext session) : this()
    {
        _session = session;
        this.FindControl<Button>("CloseBtn")!.Click += (_, _) => Close();
        this.FindControl<Button>("RefreshBtn")!.Click += async (_, _) => await YukleAsync();
    }

    /// <summary>Çakışma ekranını açar (uyarıdan veya menüden).</summary>
    public static async Task GosterAsync(SessionContext session, Window sahip)
    {
        var win = new SyncConflictsWindow(session);
        _ = win.YukleAsync();
        await win.ShowDialog(sahip);
    }

    /// <summary>Ana pencere sahipli kısa yol.</summary>
    public static async Task GosterAsync(SessionContext session)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d
            || d.MainWindow is null) return;
        await GosterAsync(session, d.MainWindow);
    }

    private async Task YukleAsync()
    {
        var durum = this.FindControl<TextBlock>("StatusText")!;
        var liste = this.FindControl<ItemsControl>("List")!;
        durum.Text = "Yükleniyor…";
        liste.ItemsSource = null;

        var ham = await BusinessSyncPushService.GetConflictsAsync();
        var yetkili = _session is not null && AccessControl.CanUseButton(_session, SpecialButtons.ConflictResolve);

        var kazananYap = new AsyncRelayCommand<CakismaSatiri>(async s => await KazananYapAsync(s));
        var gizle = new AsyncRelayCommand<CakismaSatiri>(async s => await GizleAsync(s));
        var gecmis = new AsyncRelayCommand<CakismaSatiri>(async s => await GecmisAsync(s));

        var satirlar = new List<CakismaSatiri>();
        foreach (var e in ham)
        {
            if (Str(e, "status") is "resolved") continue;
            var cozulebilir = Bool(e, "canPromoteLoser");
            satirlar.Add(new CakismaSatiri
            {
                Id = Str(e, "id"),
                Baslik = Str(e, "entityLabel"),
                Tarih = Str(e, "dateText"),
                KazananMetni = "Kazanan: " + Str(e, "winnerWho") + " — " + Str(e, "winnerText"),
                KaybedenMetni = "Kaybeden: " + Str(e, "loserWho") + " — " + Str(e, "loserText"),
                Farklar = Farklar(e),
                Not = !cozulebilir
                    ? "Bu çakışma eski sürümde oluştuğu için üzerine yazılan kaydın verisi saklanmamış; kazananı değiştirme yapılamaz."
                    : yetkili ? "" : "Kazananı değiştirmek için \"Senkron Çakışmasını Çözme\" yetkisi gerekir.",
                CozebilirMi = cozulebilir && yetkili,
                EntityType = Str(e, "entityType"),
                EntityId = Str(e, "entityId"),
                AuditEntityType = Str(e, "auditEntityType") is { Length: > 0 } t ? t : null,
                KazananYapCommand = kazananYap,
                GizleCommand = gizle,
                GecmisCommand = gecmis,
            });
        }

        liste.ItemsSource = satirlar;
        durum.Text = satirlar.Count == 0
            ? "Açık senkron çakışması yok. (Sunucuya bağlı değilseniz liste boş görünür.)"
            : $"{satirlar.Count} açık çakışma.";
    }

    private async Task KazananYapAsync(CakismaSatiri? s)
    {
        if (s is null) return;
        var onay = await ConfirmService.AskAsync(
            $"\"{s.Baslik}\" kaydında ÜZERİNE YAZILAN sürüm geri getirilecek ve kazanan olacak.\n\n" +
            "Kayıt silinmez; yalnız alan değerleri o sürüme döner ve değişiklik tüm cihazlara yayılır.\n" +
            "Devam edilsin mi?", "Senkron Çakışması", "Evet, kazanan yap", "Vazgeç", danger: true);
        if (!onay) return;

        var (ok, mesaj) = await BusinessSyncPushService.PromoteLoserAsync(s.Id);
        await ConfirmService.InfoAsync(ok
            ? "Üzerine yazılan sürüm geri getirildi ve kazanan yapıldı. Değişiklik bir sonraki eşitlemede tüm cihazlara ulaşır."
            : mesaj, "Senkron Çakışması", danger: !ok);
        if (ok) await YukleAsync();
    }

    private async Task GizleAsync(CakismaSatiri? s)
    {
        if (s is null) return;
        var onay = await ConfirmService.AskAsync(
            "Bu çakışma uyarısı listeden kaldırılacak. KAYIT DEĞİŞMEZ — yalnız uyarı kapanır. Devam edilsin mi?",
            "Senkron Çakışması", "Evet, gizle", "Vazgeç");
        if (!onay) return;
        var (ok, mesaj) = await BusinessSyncPushService.HideConflictAsync(s.Id);
        if (!ok) await ConfirmService.InfoAsync(mesaj, "Senkron Çakışması", danger: true);
        await YukleAsync();
    }

    /// <summary>Kaydın FAZ 4.3 log ekranı — çakışan kayıtta gerçekte ne değiştiği oradan izlenir.</summary>
    private async Task GecmisAsync(CakismaSatiri? s)
    {
        if (s is null || _session is null || s.AuditEntityType is null) return;
        await RecordHistoryWindow.KayitGecmisiniGosterAsync(_session, s.AuditEntityType, s.EntityId, s.Baslik, this);
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static List<string> Farklar(JsonElement e)
    {
        var liste = new List<string>();
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty("differences", out var d)
            && d.ValueKind == JsonValueKind.Array)
            foreach (var x in d.EnumerateArray())
                if (x.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    liste.Add("• " + (t.GetString() ?? ""));
        return liste;
    }

    private static bool Bool(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.True;

    private static string Str(JsonElement e, string key)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ValueKind is JsonValueKind.Null ? "" : v.ToString())
            : "";
}
