using DepoWise.Application.Common;

namespace DepoWise.Web.Services;

/// <summary>
/// ⭐ M-11 (AR-GE 2026-10-18): bu tarayıcı oturumunun (devre) gerçek kullanıcı adresi. Etkileşimli devrede HttpContext
/// yoktur; adres ilk sayfa isteğinde <c>App.razor</c>'da okunur, korumalı bileşen parametresiyle <c>Routes</c>'a geçer
/// ve burada devre boyunca saklanır.
/// </summary>
public sealed class IstemciBaglami
{
    public string? Adres { get; set; }
}

/// <summary>
/// ⭐ M-11: API'ye giden her isteğe kullanıcının adresini + imzasını ekler (<see cref="IstemciAdresiImzasi"/>). Anahtar
/// tanımlı değilse ya da adres bilinmiyorsa hiçbir şey eklemez (API eskisi gibi web sunucusunun adresini görür).
/// </summary>
public sealed class IstemciAdresiIsleyici : DelegatingHandler
{
    private readonly IstemciBaglami _baglam;
    private readonly string? _anahtar;

    public IstemciAdresiIsleyici(IstemciBaglami baglam, string? anahtar, HttpMessageHandler ic) : base(ic)
    {
        _baglam = baglam;
        _anahtar = anahtar;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var adres = _baglam.Adres;
        if (_anahtar is not null && !string.IsNullOrWhiteSpace(adres))
        {
            request.Headers.Remove(IstemciAdresiImzasi.AdresBasligi);
            request.Headers.Remove(IstemciAdresiImzasi.ImzaBasligi);
            request.Headers.TryAddWithoutValidation(IstemciAdresiImzasi.AdresBasligi, adres);
            request.Headers.TryAddWithoutValidation(IstemciAdresiImzasi.ImzaBasligi, IstemciAdresiImzasi.Imzala(_anahtar, adres));
        }
        return base.SendAsync(request, cancellationToken);
    }
}
