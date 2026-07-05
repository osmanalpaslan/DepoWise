using System;
using DepoWise.Api;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace DepoWise.Tests;

/// <summary>JWT üretimi + süre + yenileme (kayan oturum) davranışı.</summary>
public class JwtTokenTests
{
    private const string Key = "test-signing-key-32-characters-minimum-xyz";

    [Fact]
    public void Issue_SubVeCompanyClaimTasir_Ve12SaatGecerli()
    {
        var token = JwtTokens.Issue(Key, "u1", "ACME");
        var exp = JwtTokens.ReadExpiry(token);
        Assert.NotNull(exp);
        var hours = (exp!.Value - DateTime.UtcNow).TotalHours;
        Assert.InRange(hours, 11.5, 12.5);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("u1", jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == JwtTokens.CompanyClaim && c.Value == "ACME");
    }

    [Fact]
    public void GecerliToken_ValidationParametreleriyleDogrulanir()
    {
        var token = JwtTokens.Issue(Key, "u1", "ACME");
        var handler = new JwtSecurityTokenHandler();
        var principal = handler.ValidateToken(token, JwtTokens.ValidationParameters(Key), out _);
        Assert.NotNull(principal);
    }

    [Fact]
    public void FarkliAnahtarlaImzalananToken_Reddedilir()
    {
        var token = JwtTokens.Issue("baska-anahtar-32-characters-minimum-abcd", "u1", "ACME");
        var handler = new JwtSecurityTokenHandler();
        Assert.ThrowsAny<Exception>(() =>
            handler.ValidateToken(token, JwtTokens.ValidationParameters(Key), out _));
    }

    [Fact]
    public void Refresh_KimligiKorur_YeniSureUretir()
    {
        // Yenileme = aynı kullanıcı/firma için yeni token (sunucu oturumdan üretir).
        var first = JwtTokens.Issue(Key, "u1", "ACME");
        System.Threading.Thread.Sleep(1100); // exp saniye çözünürlüğü — farkı görebilmek için
        var refreshed = JwtTokens.Issue(Key, "u1", "ACME");

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshed);
        Assert.Equal("u1", jwt.Subject);
        Assert.Contains(jwt.Claims, c => c.Type == JwtTokens.CompanyClaim && c.Value == "ACME");
        // Yeni token'ın son geçerliliği ilkinden ileri (kayan oturum).
        Assert.True(JwtTokens.ReadExpiry(refreshed) >= JwtTokens.ReadExpiry(first));
    }
}
