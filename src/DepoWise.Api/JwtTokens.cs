using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace DepoWise.Api;

/// <summary>
/// JWT üretimi (durum tutmayan oturum). Token yalnız kullanıcı + firma taşır; yetkiler SUNUCUDA
/// her istekte AuthService.CreateSessionForUser ile yeniden yüklenir (token kurcalanamaz).
/// </summary>
public static class JwtTokens
{
    public const string CompanyClaim = "company";

    /// <summary>Erişim token'ı ömrü (saat). Masaüstü, süresi dolmadan /api/auth/refresh ile yeniler
    /// (uzun oturumda sync'in sessizce durmasını önler).</summary>
    public const int ExpiryHours = 12;

    public static string Issue(string signingKey, string userId, string companyId)
    {
        var creds = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId),
            new Claim(CompanyClaim, companyId),
        };
        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddHours(ExpiryHours),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Token'ın son geçerlilik anını (UTC) doğrulama YAPMADAN okur — istemci tarafı yenileme
    /// zamanlaması içindir (ne zaman refresh çağrılacağını bilmek için). Okunamazsa null.</summary>
    public static DateTime? ReadExpiry(string token)
    {
        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
            return jwt.ValidTo == DateTime.MinValue ? null : jwt.ValidTo.ToUniversalTime();
        }
        catch { return null; }
    }

    public static TokenValidationParameters ValidationParameters(string signingKey) => new()
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ClockSkew = TimeSpan.FromMinutes(1),
    };
}
