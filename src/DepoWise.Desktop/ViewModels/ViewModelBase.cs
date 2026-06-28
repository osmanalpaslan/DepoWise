using CommunityToolkit.Mvvm.ComponentModel;
using DepoWise.Application.Security;

namespace DepoWise.Desktop.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    /// <summary>
    /// Satır içi "+" (tanım ekleme) butonlarının görünürlüğü. Admin bypass + açık "+" izni; aksi halde gizli
    /// (deny-by-default). Tüm view'ler ortak bu özelliğe bağlanır. Oturum login'de yüklenir → view kurulurken sabit.
    /// </summary>
    public bool CanAddLookup =>
        DesktopServices.Session is { } s && AccessControl.CanUseButton(s, SpecialButtons.AddLookup);
}
