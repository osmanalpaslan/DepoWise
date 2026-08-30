using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepoWise.Application.Security;
using DepoWise.Application.Teams;

namespace DepoWise.Desktop.ViewModels;

/// <summary>Üye satırı — kullanıcı adı yerelde çözülemeyebilir (<c>users</c> masaüstüne inmez),
/// bu durumda kimlik gösterilir. Ayna yine de tutarlıdır (o alanlar yerelde FK değildir).
/// AXAML <c>x:DataType</c>'ın çözebilmesi için üst seviye tiptir (iç içe tip değil).</summary>
public sealed record TeamMemberRow(string UserId, string Display, bool IsLead);

/// <summary>
/// ═══ ARA İŞ 5 / ALT FAZ 1 (ADR-187) — EKİPLER (masaüstü) ═══
///
/// <b>SALT OKUNUR ekrandır.</b> Ekip verisi <b>sunucu otoritelidir</b> ve masaüstüne
/// <c>/api/lookups/sync</c> aynasıyla iner (şube/menü ayarlarıyla aynı desen). Masaüstü bu veriyi
/// YAZMAZ: yazsaydı iki tarafın aynı satırı değiştirmesi bir çakışma/LWW modeli gerektirirdi —
/// ADR-187 ise böyle bir model kurulmamasını şart koşar. Bu yüzden ekleme/düzenleme/silme
/// yalnız web (sunucu) tarafındadır; burada yalnız görüntülenir ve çevrimdışı da okunabilir.
///
/// <b>Onay ile bağı YOKTUR:</b> ekip organizasyonel gruplamadır; onay zinciri kullanıcı
/// hiyerarşisinden çözülür ve ekip lideri otomatik onaycı değildir (ADR-187 §3/§5).
///
/// <b>Yetki (PK-EK-07=B):</b> yeni modül yok — mevcut <c>users</c> modülü. Asıl kapı serviste.
/// </summary>
public sealed partial class TeamsViewModel : ViewModelBase
{
    private readonly SessionContext _session;

    public TeamsViewModel(SessionContext session)
    {
        _session = session;
        Teams = new ObservableCollection<Team>();
        Members = new ObservableCollection<TeamMemberRow>();
        Yenile();
    }

    public ObservableCollection<Team> Teams { get; }
    public ObservableCollection<TeamMemberRow> Members { get; }

    [ObservableProperty] private Team? _selectedTeam;
    [ObservableProperty] private string _message = "";

    /// <summary>Görüntüleme yetkisi — yoksa liste boş kalır ve açıklama gösterilir.</summary>
    public bool CanView => AccessControl.Can(_session, "users", PermissionAction.View);

    partial void OnSelectedTeamChanged(Team? value) => UyeleriYukle(value);

    [RelayCommand]
    public void Yenile()
    {
        Teams.Clear();
        Members.Clear();
        if (!CanView)
        {
            Message = "Ekipleri görüntüleme yetkiniz bulunmuyor.";
            return;
        }
        try
        {
            foreach (var t in DesktopServices.Teams.List(_session, includeInactive: true)) Teams.Add(t);
            Message = Teams.Count == 0
                ? "Ekip bulunamadı. Ekipler web üzerinden tanımlanır ve eşitleme ile buraya iner."
                : "";
            SelectedTeam = Teams.FirstOrDefault();
        }
        catch (ForbiddenException)
        {
            Message = "Ekipleri görüntüleme yetkiniz bulunmuyor.";
        }
        catch
        {
            // Ayna henüz inmemiş olabilir (yeni kurulum / hiç eşitlenmemiş makine).
            Message = "Ekip bilgisi yerelde yok. Sunucuya bağlanıp eşitleyin.";
        }
    }

    private void UyeleriYukle(Team? team)
    {
        Members.Clear();
        if (team is null || !CanView) return;
        try
        {
            foreach (var m in DesktopServices.Teams.Members(_session, team.Id))
                Members.Add(new TeamMemberRow(m.UserId, AdCoz(m.UserId), m.IsLead));
        }
        catch { /* ayna eksikse üye listesi boş kalır; ekran çalışmaya devam eder */ }
    }

    /// <summary>Kullanıcı adını YERELDEN çözmeye çalışır; yoksa kimliği gösterir.
    /// <c>users</c> masaüstüne senkronlanmadığı için bu çoğu makinede kimlik olarak kalır.</summary>
    private static string AdCoz(string userId)
    {
        try
        {
            using var conn = DesktopServices.Factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT username FROM users WHERE id=@i;";
            var p = cmd.CreateParameter(); p.ParameterName = "@i"; p.Value = userId; cmd.Parameters.Add(p);
            var v = cmd.ExecuteScalar();
            return v is string s && s.Length > 0 ? s : userId;
        }
        catch { return userId; }
    }
}
