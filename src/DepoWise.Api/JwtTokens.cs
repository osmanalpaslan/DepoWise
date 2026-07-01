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
            expires: DateTime.UtcNow.AddHours(12),
            signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
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
