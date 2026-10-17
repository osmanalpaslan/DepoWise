using System.Text.Json;

namespace DepoWise.Web.Services;

/// <summary>
/// ⭐ M-04 (AR-GE 2026-10-17) — web formu için işlem kimliği (operationId).
///
/// <b>Sorun:</b> sunucu, web'den gelen yakıt/bakım/günlük faaliyet kayıtlarında kimliği her istekte kendisi üretiyordu →
/// çift tıklama ya da zaman aşımından sonra "tekrar dene" İKİ kayıt (ve malzemede iki stok düşümü) oluşturuyordu.
///
/// <b>Kural:</b> aynı form AYNI bilgilerle tekrar gönderilirse AYNI kimlik gider (sunucu var olan kaydı döndürür,
/// ikinci kayıt oluşmaz). Bilgiler DEĞİŞTİYSE yeni kimlik üretilir — aksi hâlde zaman aşımından sonra düzeltilmiş
/// bilgiyle yapılan gönderim eski kaydı döndürür ve yeni bilgi sessizce kaybolurdu. Kayıt başarılı olunca
/// <see cref="Tamamlandi"/> çağrılır → sonraki kayıt yeni kimlik alır.
/// </summary>
public sealed class IslemKimligi
{
    private string _kimlik = Yeni();
    private string? _sonGovde;

    private static string Yeni() => Guid.NewGuid().ToString("N");

    /// <summary>Gönderilecek gövde (kimlik HARİÇ) için kullanılacak kimliği döndürür.</summary>
    public string Icin(object govde)
    {
        var j = JsonSerializer.Serialize(govde);
        if (_sonGovde is not null && !string.Equals(j, _sonGovde, StringComparison.Ordinal)) _kimlik = Yeni();
        _sonGovde = j;
        return _kimlik;
    }

    /// <summary>Anonim/sözlük gövdeye <c>operationId</c> ekleyip gönderilecek JSON nesnesini döndürür
    /// (kimlik, gövdenin kimliksiz hâline göre <see cref="Icin"/> ile seçilir).</summary>
    public System.Text.Json.Nodes.JsonObject Ekle(object govde)
    {
        var kimlik = Icin(govde);
        var o = JsonSerializer.SerializeToNode(govde)!.AsObject();
        o["operationId"] = kimlik;
        return o;
    }

    /// <summary>Kayıt başarılı oldu → sonraki kayıt yeni kimlikle başlar.</summary>
    public void Tamamlandi() { _kimlik = Yeni(); _sonGovde = null; }
}
