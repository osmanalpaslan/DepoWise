using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DepoWise.Api;

/// <summary>
/// ═══ ANLIK SOHBET (kullanıcı isteği 2026-09-07) ═══
///
/// <para><b>Neden var:</b> sohbet 3 saniyede bir yoklama yapıyordu. Kullanıcı: <i>"mesajın 3 saniye
/// süreci zaten can sıkıcı, anlık olması hoşuma gider."</i> Yoklama hem gecikme üretir hem de
/// kullanıcı başına dakikada 20 boş istek demektir.</para>
///
/// <para><b>Ne taşır:</b> <b>SADECE BİR İŞARET</b> — "sana X kişisinden yeni bir şey var".
/// <b>Mesaj içeriği bu kanaldan GEÇMEZ.</b> İstemci işareti alınca mesajı normal, yetkisi
/// denetlenen <c>/api/chat/messages</c> ucundan çeker. Böylece yetki/tenant kararı TEK yerde
/// (ChatService + AccessControl) kalır; ikinci bir veri yolu ve ikinci bir güvenlik yüzeyi
/// açılmaz.</para>
///
/// <para><b>Grup adı firma + kullanıcıdır</b> (<see cref="Grup"/>). Yalnız alıcının kendi grubuna
/// gönderilir; farklı firmadaki aynı kullanıcı kimliği bile aynı gruba düşemez.</para>
///
/// <para><b>Yoklama KALDIRILMADI, YAVAŞLATILDI.</b> Bağlantı koparsa (ağ, proxy, uyku) istemci
/// yine de mesajları görür — yalnız biraz geç. Tek kanala bağlı kalmak, sohbeti sessizce
/// öldürebilecek bir tekil arıza noktası olurdu.</para>
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    /// <summary>SignalR yolu — istemciler buraya bağlanır.</summary>
    public const string Yol = "/hubs/chat";

    /// <summary>İstemcide çağrılan metodun adı. Tek parametre: gönderenin kullanıcı kimliği.</summary>
    public const string MesajGeldiMetodu = "MesajGeldi";

    /// <summary>Bir kullanıcının kişisel grubu. Firma kimliği İÇERİR → tenant sınırı grup adındadır.</summary>
    public static string Grup(string companyId, string userId) => $"chat:{companyId}:{userId}";

    public override async Task OnConnectedAsync()
    {
        var (firma, kullanici) = Kimlik();
        if (firma is not null && kullanici is not null)
            await Groups.AddToGroupAsync(Context.ConnectionId, Grup(firma, kullanici));
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        // SignalR grup üyeliğini bağlantı kapanınca kendisi temizler; yine de açık olalım.
        var (firma, kullanici) = Kimlik();
        if (firma is not null && kullanici is not null)
            try { await Groups.RemoveFromGroupAsync(Context.ConnectionId, Grup(firma, kullanici)); } catch { }
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Kimlik YALNIZ doğrulanmış jetondan okunur (CLAUDE.md §4: company_id güvenilir bağlamdan gelir).
    /// İstemcinin gönderdiği hiçbir değer burada kullanılmaz.
    /// </summary>
    private (string? Firma, string? Kullanici) Kimlik()
    {
        var u = Context.User;
        if (u is null) return (null, null);
        var firma = u.FindFirst(JwtTokens.CompanyClaim)?.Value;
        var kullanici = u.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                        ?? u.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        return (string.IsNullOrWhiteSpace(firma) ? null : firma,
                string.IsNullOrWhiteSpace(kullanici) ? null : kullanici);
    }
}
