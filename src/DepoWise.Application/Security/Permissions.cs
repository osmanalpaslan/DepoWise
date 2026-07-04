namespace DepoWise.Application.Security;

/// <summary>Tek modül için 4 işlem bayrağı (user_permissions satırı).</summary>
public sealed record ModulePermission(
    string ModuleKey,
    bool CanView,
    bool CanCreate,
    bool CanEdit,
    bool CanDelete);

/// <summary>
/// Bir kullanıcının yetki kümesi + özel buton izinleri. Deny-by-default:
/// kayıt yoksa erişim yoktur. Admin/Süper Admin bypass değerlendirici tarafında uygulanır.
/// </summary>
public sealed class PermissionSet
{
    private readonly Dictionary<string, ModulePermission> _modules;
    private readonly HashSet<string> _buttons;

    public PermissionSet(IEnumerable<ModulePermission> modules, IEnumerable<string>? buttons = null)
    {
        _modules = modules.ToDictionary(m => m.ModuleKey, StringComparer.Ordinal);
        _buttons = new HashSet<string>(buttons ?? Array.Empty<string>(), StringComparer.Ordinal);
    }

    public static PermissionSet Empty { get; } = new(Array.Empty<ModulePermission>());

    public ModulePermission? For(string moduleKey)
        => _modules.TryGetValue(moduleKey, out var p) ? p : null;

    public bool HasButton(string buttonKey) => _buttons.Contains(buttonKey);

    /// <summary>Tüm modül izinleri (senkron/dışa aktarım için salt okuma).</summary>
    public IEnumerable<ModulePermission> Modules => _modules.Values;

    /// <summary>Tüm özel buton izinleri (senkron/dışa aktarım için salt okuma).</summary>
    public IEnumerable<string> Buttons => _buttons;
}
