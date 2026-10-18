namespace DepoWise.Infrastructure.Database;

/// <summary>
/// ⭐ M-14 (AR-GE 2026-10-18, 05 F-063) — EXCEL İÇE AKTARIM PARTİSİ.
///
/// İçe aktarım satır satır kaydeder (bir hatalı satır diğerlerini bozmaz) ama sonradan "bu aktarımda ne oluştu" bilinmiyordu
/// → yanlış dosya aktarılınca kayıtlar tek tek elle silinmek zorundaydı. Aktarım süresince bu bağlam açılır;
/// <see cref="AuditWriter"/> o sürede yazılan her işlem geçmişi satırına parti kimliğini (<c>correlation_id</c>) damgalar.
/// Geri alma (<c>AktarimGeriAlmaService</c>) partide OLUŞAN kayıtları buradan bulur. Yeni tablo/migration gerekmez.
///
/// Bağlam akışa (async flow) özeldir: aynı anda çalışan başka işlemler (eşitleme, diğer kullanıcılar) damgalanmaz.
/// </summary>
public static class AktarimPartisi
{
    public const string Onek = "import:";
    private static readonly AsyncLocal<string?> _gecerli = new();

    /// <summary>Açık parti kimliği (yoksa null).</summary>
    public static string? Gecerli => _gecerli.Value;

    /// <summary>Partiyi açar; dönen nesne kapatılınca (using) önceki duruma döner. <paramref name="tur"/> = aktarım türü
    /// anahtarı (materials, vehicles…). Kimlik: <c>import:&lt;tür&gt;:&lt;guid&gt;</c>.</summary>
    public static Kapsam Baslat(string tur)
    {
        var kimlik = $"{Onek}{tur}:{Guid.NewGuid():N}";
        var onceki = _gecerli.Value;
        _gecerli.Value = kimlik;
        return new Kapsam(kimlik, onceki);
    }

    public sealed class Kapsam : IDisposable
    {
        private readonly string? _onceki;
        private bool _kapandi;
        public string Kimlik { get; }
        internal Kapsam(string kimlik, string? onceki) { Kimlik = kimlik; _onceki = onceki; }
        public void Dispose()
        {
            if (_kapandi) return;
            _kapandi = true;
            _gecerli.Value = _onceki;
        }
    }
}
