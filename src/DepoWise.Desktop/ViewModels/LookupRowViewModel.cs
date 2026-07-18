using CommunityToolkit.Mvvm.ComponentModel;

namespace DepoWise.Desktop.ViewModels;

/// <summary>
/// Tanımlar ekranında düzenlenebilir tek satır (kullanıcı isteği 2026-07-18: "tanım düzenle ekranında
/// düzenleme de olmalı"). Ad kutusu iki-yönlü bağlanır; "Kaydet" bölüm VM'inin Rename komutunu çağırır.
/// Id KORUNUR — yeniden adlandırma bağlı kayıtları etkilemez.
/// </summary>
public sealed partial class LookupRowViewModel : ObservableObject
{
    public string Id { get; }
    /// <summary>Yeniden adlandırma çakışırsa/başarısız olursa eski ada dönmek için.</summary>
    public string OriginalName { get; }
    [ObservableProperty] private string _name;
    /// <summary>Sabit tanım — kilitliyken düzenlenemez/silinemez (kullanıcı isteği 2026-07-19).</summary>
    [ObservableProperty] private bool _isLocked;

    public LookupRowViewModel(string id, string name, bool isLocked = false) { Id = id; OriginalName = name; _name = name; _isLocked = isLocked; }
}
