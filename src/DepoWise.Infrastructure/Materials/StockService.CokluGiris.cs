namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// ⭐ Kullanıcı isteği 2026-10-24 (yayın öncesi 2/2): Giriş-Çıkış "Yeni Kayıt"ta da Depo Çıkışı'ndaki gibi BİRDEN FAZLA malzeme.
/// Sepetteki mevcut malzemeler + formdaki kalem TEK giriş belgesinde işlenir. Satır kuralı tek yerde (masaüstü ve API aynı):
/// miktar &gt; 0; aynı malzeme birden çok satırdaysa tek satırda toplanır, birim fiyat miktarla ağırlıklı ortalamadır
/// (fiyatsız satırlar ortalamaya girmez; hiç fiyat yoksa fiyatsız).
/// </summary>
public sealed partial class StockService
{
    public const int MaxGirisSatiri = 200;

    public static IReadOnlyList<StockLine> GirisSatirlari(IEnumerable<StockLine> satirlar)
    {
        var liste = satirlar.ToList();
        if (liste.Any(l => string.IsNullOrWhiteSpace(l.MaterialId))) throw new ArgumentException("Malzeme seçin.");
        if (liste.Any(l => l.Quantity <= 0)) throw new ArgumentException("Miktar sıfırdan büyük olmalı.");
        if (liste.Any(l => l.UnitPrice is < 0)) throw new ArgumentException("Birim fiyat negatif olamaz.");
        var sonuc = liste.GroupBy(l => l.MaterialId, StringComparer.Ordinal).Select(g =>
        {
            var miktar = g.Sum(x => x.Quantity);
            var fiyatli = g.Where(x => x.UnitPrice is > 0).ToList();
            decimal? fiyat = fiyatli.Count == 0 ? null
                : Math.Round(fiyatli.Sum(x => x.Quantity * x.UnitPrice!.Value) / fiyatli.Sum(x => x.Quantity), 4);
            return new StockLine(g.Key, miktar, fiyat, g.First().Currency);
        }).ToList();
        if (sonuc.Count > MaxGirisSatiri) throw new ArgumentException($"Bir girişte en fazla {MaxGirisSatiri} malzeme olabilir.");
        return sonuc;
    }
}
