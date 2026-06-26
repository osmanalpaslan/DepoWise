using System.Text.Json.Serialization;

namespace DepoWise.Application.Common;

/// <summary>
/// Tüm /api/v1 uçlarının ve masaüstü servis sınırlarının kullandığı ortak hata modeli.
/// Web ve masaüstü aynı kodları/biçimi üretir (fonksiyonel eşitlik).
/// </summary>
public sealed class ApiError
{
    public string Code { get; init; } = ErrorCodes.Internal;
    public string Message { get; init; } = "Beklenmeyen bir hata oluştu.";
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>Alan bazlı doğrulama hataları (alan adı -> mesaj listesi). Opsiyonel.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string[]>? Fields { get; init; }

    public static ApiError Of(string code, string message, string correlationId,
        IReadOnlyDictionary<string, string[]>? fields = null)
        => new() { Code = code, Message = message, CorrelationId = correlationId, Fields = fields };
}

/// <summary>Web ve masaüstünde ortak hata kodları. Yeni kod eklenir, mevcut kod silinmez.</summary>
public static class ErrorCodes
{
    public const string Validation = "validation_error";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string Conflict = "conflict";
    public const string TenantViolation = "tenant_violation";
    public const string IdempotencyReplay = "idempotency_replay";
    public const string Internal = "internal_error";
}

/// <summary>Keyset (cursor) pagination için kararlı sayfalama sözleşmesi.</summary>
public sealed class PageRequest
{
    public int Limit { get; init; } = 50;
    /// <summary>Opak ileri-yön imleci; null ise ilk sayfa.</summary>
    public string? Cursor { get; init; }

    public const int MaxLimit = 200;

    public int NormalizedLimit() => Limit < 1 ? 1 : (Limit > MaxLimit ? MaxLimit : Limit);
}

/// <summary>Keyset pagination sonucu. Toplam sayı zorunlu değildir (kararlı/benzersiz sıralama esas).</summary>
public sealed class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public string? NextCursor { get; init; }
    public bool HasMore => NextCursor is not null;

    public static PagedResult<T> Of(IReadOnlyList<T> items, string? nextCursor)
        => new() { Items = items, NextCursor = nextCursor };
}
