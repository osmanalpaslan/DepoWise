using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>⭐ M-49: okutulan koda karşılık gelen kayıt.</summary>
public sealed record OkunanKayit(string Tur, string Id, string Kod, string Ad);

/// <summary>
/// ═══ M-49 (AR-GE 2026-10-19, 05 F-139 / F-026) — FORMDA QR / BARKOD OKUTARAK KAYIT SEÇME ═══
/// QR etiketi (<see cref="QrLabelService"/>) yalnız kaydın KODUNU taşır; USB okuyucu kodu yazıp Enter'a basar. Formdaki
/// "Kod okut" kutusu bu servise sorar: araçta iç kod ya da plaka (boşluksuz), ekipman/malzemede kod — büyük/küçük harf
/// duyarsız, TAM eşleşme (kısmi eşleşmeyle yanlış kayıt seçilmesin). Firma filtresi oturumdan; modülü görme yetkisi gerekir.
/// Kaydı yalnız SEÇER — kayıt işlemi formun kendi kurallarından (yetki, şube, onay) geçer.
/// </summary>
public sealed class KodOkutmaService
{
    public const string Arac = "vehicle", Ekipman = "equipment", Malzeme = "material";
    private readonly IDbConnectionFactory _factory;

    public KodOkutmaService(IDbConnectionFactory factory) => _factory = factory;

    /// <summary>Okuyucunun eklediği satır sonu/boşluk/denetim karakterlerini temizler.</summary>
    public static string Temizle(string? okunan)
        => new string((okunan ?? "").Where(ch => !char.IsControl(ch)).ToArray()).Trim();

    /// <summary>Koda karşılık gelen kayıt; yoksa null.</summary>
    public OkunanKayit? Bul(SessionContext s, string tur, string? okunan)
    {
        var kod = Temizle(okunan);
        if (kod.Length == 0) throw new ArgumentException("Okutulan kod boş.");
        var (modul, sql) = tur switch
        {
            Arac => ("vehicles",
                "SELECT id, internal_code, COALESCE(plate,'') FROM vehicles WHERE company_id=@c AND is_deleted=0 AND " +
                "(UPPER(internal_code)=UPPER(@k) OR REPLACE(UPPER(COALESCE(plate,'')),' ','')=REPLACE(UPPER(@k),' ','')) " +
                "ORDER BY CASE WHEN UPPER(internal_code)=UPPER(@k) THEN 0 ELSE 1 END, internal_code LIMIT 2;"),
            Ekipman => (EquipmentService_Module,
                "SELECT id, code, name FROM equipment WHERE company_id=@c AND is_deleted=0 AND UPPER(code)=UPPER(@k) ORDER BY code LIMIT 2;"),
            Malzeme => ("materials",
                "SELECT id, code, name FROM materials WHERE company_id=@c AND is_deleted=0 AND UPPER(code)=UPPER(@k) ORDER BY code LIMIT 2;"),
            _ => throw new ArgumentException("Geçersiz kayıt türü."),
        };
        AccessControl.Require(s, modul, PermissionAction.View);
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.AddWithValue("@c", s.CompanyId);
        cmd.AddWithValue("@k", kod);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var bulunan = new OkunanKayit(tur, r.GetString(0), r.GetString(1), r.IsDBNull(2) ? "" : r.GetString(2));
        if (!r.Read()) return bulunan;
        // İkinci eşleşme var: yalnız araçta "biri iç kodla, diğeri plakayla" eşleştiyse iç kod kazanır; başka her durum
        // belirsizdir → hiçbir kayıt seçilmez (yanlış araca/alete işlem yazılmasın).
        static bool KodEsit(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        return tur == Arac && KodEsit(bulunan.Kod, kod) && !KodEsit(r.GetString(1), kod) ? bulunan : null;
    }

    private const string EquipmentService_Module = DepoWise.Infrastructure.Equipment.EquipmentService.Module;
}
