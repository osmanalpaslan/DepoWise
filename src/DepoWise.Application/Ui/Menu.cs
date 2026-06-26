using DepoWise.Application.Security;

namespace DepoWise.Application.Ui;

public sealed record MenuItem(string Key, string Label);

/// <summary>
/// Menü kurucu — deny-by-default. Yalnız oturumun okuma (CanSeeMenu) yetkisi olan modülleri
/// üretir. Web ve masaüstü AYNI sonucu kullanır (tek doğru kaynak: AppModules + AccessControl).
/// </summary>
public static class MenuBuilder
{
    public static IReadOnlyList<MenuItem> Build(SessionContext session)
    {
        var items = new List<MenuItem>();
        foreach (var (key, label) in AppModules.All)
        {
            if (AccessControl.CanSeeMenu(session, key))
                items.Add(new MenuItem(key, label));
        }
        return items;
    }
}
