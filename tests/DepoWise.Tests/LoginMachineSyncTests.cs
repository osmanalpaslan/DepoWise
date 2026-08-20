using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ B — GİRİŞ · MAKİNE · EŞİTLEME KİLİTLERİ (saha bulgusu 2026-08-19) ═══
///
/// Kullanıcı makineleri önce sıfırlayıp sonra sildi. Ardından:
/// <list type="number">
///   <item>babası internet varken giremedi ("makine ilk kez kuruluyor, internet gerekli"),</item>
///   <item>kendi makinesinde ŞUBE SEÇİM EKRANI HİÇ GELMEDİ — makinenin önbellekteki eski şubesine
///   sessizce girildi,</item>
///   <item>silinmiş test kayıtlarına ait "6 kayıt gönderilemiyor" uyarısı temizlenemiyordu.</item>
/// </list>
///
/// <b>Ortak kök neden:</b> ağ isteği zaman aşımına uğrayınca uygulama kendini <b>çevrimdışı</b> sayıyor
/// ve giriş akışı sessiz dallara giriyordu. Bu testler düzeltmelerin kaynakta durduğunu kilitler
/// (davranış testi değil: ağ/GUI gerektiren yollar birim testine uygun değil).
/// </summary>
public class LoginMachineSyncTests
{
    private static string Root()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "DepoWise.sln"))) return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Depo kökü bulunamadı.");
    }

    private static string Read(string rel)
        => File.ReadAllText(Path.Combine(Root(), rel.Replace('/', Path.DirectorySeparatorChar)));

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // B1 · SUNUCUYA ULAŞILAMADI ≠ ÇEVRİMDIŞI
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// L1 — Giriş yolundaki ağ zaman aşımları, sunucu veritabanı uykudan uyanırken yetecek kadar
    /// uzun olmalı. 6 sn / 10 sn sahada yetmedi ve uygulama çevrimdışı sanıldı.
    /// </summary>
    [Fact]
    public void L1_Giris_Zaman_Asimlari_Yeterince_Uzun()
    {
        Assert.Contains("Timeout = TimeSpan.FromSeconds(20)", Read("src/DepoWise.Desktop/MachineGate.cs"));
        Assert.Contains("Timeout = TimeSpan.FromSeconds(25)", Read("src/DepoWise.Desktop/ServerAuthClient.cs"));
        // İlk deneme başarısız olursa bir kez daha denenir (uyandırma isteği sunucuyu ayağa kaldırır).
        Assert.Contains("deneme <= 2", Read("src/DepoWise.Desktop/MachineGate.cs"));
    }

    /// <summary>
    /// ⭐ L2 — Sunucu kimliği DOĞRULADIKTAN sonra yapılan YEREL aynalama adımları patlarsa oturum
    /// "çevrimdışı" sayılmamalı. Eskiden bu adımlar dıştaki tek catch içindeydi ve her hata
    /// ağ hatası gibi ele alınıyordu.
    /// </summary>
    [Fact]
    public void L2_Yerel_Ayna_Hatasi_Cevrimdisi_Sayilmaz()
    {
        var src = Read("src/DepoWise.Desktop/ServerAuthClient.cs");
        var i = src.IndexOf("ImportRemoteUser(bundle)", StringComparison.Ordinal);
        Assert.True(i > 0);
        // Aynalama adımları KENDİ try/catch'i içinde olmalı ve ardından yine AuthState.Ok dönülmeli.
        var oncesi = src.Substring(Math.Max(0, i - 600), Math.Min(600, i));
        Assert.Contains("try", oncesi);
        var sonrasi = src.Substring(i, Math.Min(700, src.Length - i));
        Assert.Contains("catch", sonrasi);
        Assert.Contains("AuthState.Ok", sonrasi);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // B2/B3 · ŞUBE ADIMI HER ZAMAN GÖSTERİLİR, GİRİŞ KİLİTLENMEZ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ L3 — Çevrimdışıyken makinenin önbellekteki şubesine SESSİZCE giriş YAPILMAZ. Kullanıcı
    /// hangi şubeye girdiğini görmeli; makine şubesi yalnız ön seçimdir.
    /// </summary>
    [Fact]
    public void L3_Cevrimdisi_Sessiz_Oto_Sube_Girisi_Yok()
    {
        var src = Read("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs");
        // Eski davranış: "!_online → doğrudan FinalizeLoginAsync(MachineBranchId…)" — kalmamalı.
        Assert.DoesNotContain(
            "await FinalizeLoginAsync(DesktopServices.MachineBranchId, DesktopServices.MachineBranchName, isAllBranches: false, warnOnDifferent: false);",
            src);
        // Yerine kullanıcıya durum bildirilir ve şube adımına düşülür.
        Assert.Contains("OfflineNotice", src);
        Assert.Contains("Şubeyi aşağıdan doğrulayın", src);
        Assert.Contains("HasOfflineNotice", Read("src/DepoWise.Desktop/Views/LoginWindow.axaml"));
    }

    /// <summary>
    /// ⭐ L4 — Makinenin şubesi yokken (silinip yeniden kaydolmuş makine) sunucuya ulaşılamıyorsa
    /// giriş TAMAMEN engellenmez: kullanıcının kendi şubesi biliniyorsa girer, makine ataması ertelenir.
    /// Babanın giremediği durum tam olarak buydu.
    /// </summary>
    [Fact]
    public void L4_Makine_Subesi_Yokken_Giris_Kilitlenmez()
    {
        var src = Read("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs");
        var i = src.IndexOf("if (!machineHasBranch)", StringComparison.Ordinal);
        Assert.True(i > 0);
        var govde = src.Substring(i, Math.Min(2200, src.Length - i));
        // Çevrimdışı dalda önce kullanıcının kendi şubesi kontrol edilir; yalnız o da yoksa engellenir.
        Assert.Contains("string.IsNullOrEmpty(userBranchId) && !canAll", govde);
        Assert.Contains("Makine ataması internete bağlanınca yapılacak", govde);
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // ŞB-GİRİŞ · VARSAYILAN ŞUBE + MAKİNE ŞUBESİ İŞARETİ
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ L6 (kullanıcı isteği 2026-08-20) — Giriş şube kutusunda VARSAYILAN, kullanıcının KENDİ
    /// şubesidir; kullanıcının şubesi listede yoksa makinenin şubesine düşülür. Sıra bozulursa
    /// kullanıcı her girişte yanlış şubeyle başlar.
    /// </summary>
    [Fact]
    public void L6_Varsayilan_Sube_Kullanicinin_Kendi_Subesi()
    {
        var src = Read("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs");
        var i = src.IndexOf("SelectedBranch = Branches.FirstOrDefault(b => b.Id == userBranchId)", StringComparison.Ordinal);
        Assert.True(i > 0, "Varsayılan seçim kullanıcının kendi şubesinden başlamalı.");
        // Yedek seçenek makine şubesidir ve SONRA gelir.
        var kuyruk = src.Substring(i, Math.Min(260, src.Length - i));
        Assert.Contains("MachineBranchId", kuyruk);
    }

    /// <summary>
    /// ⭐ L7 — Makinenin şubesi listede SİMGEYLE işaretlenir; kullanıcı hangisinin makine şubesi
    /// olduğunu görüp seçebilir. İşaret YALNIZ görüntüdür: kimlik/yetki/şube şifresi mantığına
    /// karışmaz (işaretleme kapsam kırpmasından SONRA çalışır).
    /// </summary>
    [Fact]
    public void L7_Makine_Subesi_Listede_Isaretlenir()
    {
        var client = Read("src/DepoWise.Desktop/ServerAuthClient.cs");
        Assert.Contains("public bool IsMachineBranch { get; set; }", client);
        Assert.Contains("MachineBranchMark", client);

        var vm = Read("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs");
        Assert.Contains("private void MarkMachineBranch()", vm);
        // Kapsam kırpmasından SONRA işaretlenmeli (kırpma listeyi değiştirir).
        var kirp = vm.IndexOf("FilterBranchesByScope();", StringComparison.Ordinal);
        var isaret = vm.IndexOf("MarkMachineBranch();", StringComparison.Ordinal);
        Assert.True(kirp > 0 && isaret > kirp, "İşaretleme kapsam kırpmasından sonra çalışmalı.");

        // Ekranda kısa açıklama var.
        Assert.Contains("bu makinenin şubesidir", Read("src/DepoWise.Desktop/Views/LoginWindow.axaml"));
    }

    // ═════════════════════════════════════════════════════════════════════════════════════════
    // B4 · EŞİTLEME UYARISI TEMİZLENEBİLİR
    // ═════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ L5 — "N kayıt gönderilemiyor" kalıcı uyarısı temizlenebilmeli ve sıfırlama işlemleri
    /// eşitleme defterini de sıfırlamalı. Sahada silinmiş test kayıtlarının uyarısı ekranda kalmıştı.
    /// </summary>
    [Fact]
    public void L5_Esitleme_Uyarisi_Temizlenebilir()
    {
        var push = Read("src/DepoWise.Desktop/BusinessSyncPushService.cs");
        Assert.Contains("public static void ClearPoison", push);

        var shell = Read("src/DepoWise.Desktop/ViewModels/ShellViewModel.cs");
        Assert.Contains("Uyarıyı Temizle", shell);
        Assert.Contains("BusinessSyncPushService.ClearPoison", shell);

        // Sıfırlama yolları eşitleme durumunu da temizler (uyarı sıfırlamayı atlatmasın).
        var purge = Read("src/DepoWise.Desktop/LocalPurgeService.cs");
        Assert.Contains("public static void ResetSyncState", purge);
        Assert.Contains("sync_push_poison", purge);
        Assert.Contains("ResetSyncState", Read("src/DepoWise.Desktop/ViewModels/LoginViewModel.cs"));
        Assert.Contains("ResetSyncState", shell);
    }
}
