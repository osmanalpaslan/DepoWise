using System.Windows.Input;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Malzeme/Araç Listesi gibi sıralanabilir + kolon-genişliği-ayarlanabilir ekranların ortak sözleşmesi
/// (kullanıcı isteği 2026-07-18, madde 3+5). Başlık hücresi (SortHeader, Controls) bu arayüz üzerinden
/// DataContext'e bağlanır — hangi VM (Materials/Vehicles) olduğunu bilmesi gerekmez.
/// </summary>
public interface IListGridViewModel
{
    /// <summary>(Sıralanan kolon anahtarı, azalan mı) — null anahtar = doğal sıra.</summary>
    (string? SortColumn, bool SortDesc) SortState { get; }
    /// <summary>Başlığa tıklama komutu; parametre = kolon anahtarı.</summary>
    ICommand SortByCommand { get; }
    /// <summary>Bu kolonun ŞU ANKİ genişliği (piksel) — kişiye özel kayıtlıysa o, yoksa varsayılan.</summary>
    double GetColumnWidth(string key);
    /// <summary>Sürükleme SIRASINDA çağrılır (henüz kalıcı değil) — canlı önizleme.</summary>
    void PreviewColumnWidth(string key, double newWidth);
    /// <summary>Sürükleme BİTİNCE çağrılır — bu anda kişiye özel kalıcı hâle gelir.</summary>
    void CommitColumnWidth();
}
