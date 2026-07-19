namespace DepoWise.Desktop.ViewModels;

/// <summary>Açık ekranın verisini yeniden yükleyebilmesi için (kullanıcı isteği 2026-07-19: eşitleme yeni
/// veri getirince liste kendini yenilesin — kullanıcı başka ekrana gidip dönmek zorunda kalmasın).
/// Kabuk (ShellViewModel), pull sunucu sürümünü değiştirdiğinde <see cref="RefreshData"/>'yı çağırır.</summary>
public interface IRefreshable
{
    void RefreshData();
}
