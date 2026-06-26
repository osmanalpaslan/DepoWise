namespace DepoWise.Application.Common;

/// <summary>Açılış/health kontrol sonucu. Masaüstü açılışında ve API /health'te kullanılır.</summary>
public sealed record HealthResult(
    bool Ok,
    string Host,
    string DatabasePath,
    string JournalMode,
    bool ForeignKeysOn,
    bool WriteReadOk,
    string? Error = null);

/// <summary>Yerel veritabanı sağlık kontrolü sözleşmesi (Infrastructure implemente eder).</summary>
public interface IDatabaseHealth
{
    Task<HealthResult> CheckAsync(CancellationToken ct = default);
}
