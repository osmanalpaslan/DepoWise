using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DepoWise.Application.Setup;

/// <summary>Kurulacak Alpnex paketi.</summary>
public sealed record SetupPackageInfo(
    string Version,
    string DownloadUrl,
    string? Sha256,
    long SizeBytes,
    string MinSupportedVersion);

/// <summary>Sistem ön-koşulu (kurulabilir bir bileşen DEĞİL — makinenin karşılaması gereken şart).</summary>
public sealed record SetupRequirement(string Id, string Label, long Value);

/// <summary>
/// Kurulabilir dış bileşen. <b>BUGÜN LİSTE BOŞTUR</b> — ölçüldü: Alpnex'in ayrıca kurulması gereken
/// hiçbir dış bağımlılığı yok (VC++ Redistributable importu yok, WebView2 kullanılmıyor, .NET paket
/// içinde). Tip yine de tanımlıdır ki ileride bir bileşen gerekirse <b>kurulum aracının kodu değil,
/// yalnız manifest</b> değişsin.
/// </summary>
public sealed record SetupDependency(
    string Id,
    string Name,
    bool Required,
    int Order,
    string? OfficialUrl,
    string? FallbackUrl,
    string? Sha256,
    string? InstallerType,
    string? SilentArgs,
    bool RequiresAdministrator);

/// <summary>Kurulum aracının sunucudan okuduğu tanım.</summary>
public sealed record SetupManifest(
    int ManifestVersion,
    SetupPackageInfo Application,
    IReadOnlyList<SetupRequirement> Requirements,
    IReadOnlyList<SetupDependency> Dependencies);

/// <summary>
/// ═══ MANIFEST OKUYUCU + GERİYE UYUMLULUK (2026-09-04) ═══
///
/// Kurulum aracı önce yeni <c>/api/setup/manifest</c> ucunu dener; yoksa <b>mevcut</b>
/// <c>/api/releases/latest</c> yanıtından aynı modeli üretir. Bu geri düşüş ZORUNLUDUR: kurulum aracı
/// ile sunucu bağımsız yayınlanır ve manifest ucu canlıya çıkmadan önce de yeni kurulum aracı çalışır.
/// </summary>
public static class SetupManifestReader
{
    /// <summary>Ön-koşul kimlikleri (sabit; manifest bunları değer olarak taşır).</summary>
    public const string ReqOsBuild = "os_build";
    public const string ReqDiskBytes = "disk_bytes";

    /// <summary>Varsayılan ön-koşullar — manifest bildirmezse bunlar geçerlidir.</summary>
    /// <remarks>
    /// 14393 = Windows 10 1607: .NET 8'in asgarisi ve <c>api-ms-win-crt-*</c> (UCRT) bileşenlerinin
    /// işletim sistemiyle birlikte geldiği ilk sürüm. Disk: ~86 MB zip + ~245 MB açılmış + pay.
    /// </remarks>
    public static IReadOnlyList<SetupRequirement> DefaultRequirements { get; } = new[]
    {
        new SetupRequirement(ReqOsBuild, "Windows 10 (1607) veya üzeri", 14393),
        new SetupRequirement(ReqDiskBytes, "En az 400 MB boş disk alanı", 400L * 1024 * 1024),
    };

    /// <summary>Mevcut <c>/api/releases/latest</c> yanıtından manifest üretir (geri düşüş yolu).</summary>
    public static SetupManifest FromReleasesLatest(string json)
    {
        var root = OkuKok(json);
        return new SetupManifest(
            ManifestVersion: 0,                      // 0 = geri düşüşle üretildi
            Application: PaketOku(root),
            Requirements: DefaultRequirements,
            Dependencies: Array.Empty<SetupDependency>());
    }

    /// <summary>Yeni <c>/api/setup/manifest</c> yanıtını okur.</summary>
    public static SetupManifest Parse(string json)
    {
        var root = OkuKok(json);

        if (!root.TryGetProperty("application", out var app))
            throw new SetupVerificationException("MANIFEST_GECERSIZ",
                "Sunucudan gelen kurulum tanımı eksik. Kurulum iptal edildi.");

        var reqs = new List<SetupRequirement>();
        if (root.TryGetProperty("requirements", out var rs) && rs.ValueKind == JsonValueKind.Array)
            foreach (var r in rs.EnumerateArray())
                if (Metin(r, "id") is { Length: > 0 } id)
                    reqs.Add(new SetupRequirement(id, Metin(r, "label") ?? id, Sayi(r, "value")));

        var deps = new List<SetupDependency>();
        if (root.TryGetProperty("dependencies", out var ds) && ds.ValueKind == JsonValueKind.Array)
            foreach (var d in ds.EnumerateArray())
                if (Metin(d, "id") is { Length: > 0 } id)
                    deps.Add(new SetupDependency(
                        id, Metin(d, "name") ?? id,
                        Bool(d, "required"), (int)Sayi(d, "order"),
                        Metin(d, "officialUrl"), Metin(d, "fallbackUrl"), Metin(d, "sha256"),
                        Metin(d, "installerType"), Metin(d, "silentArgs"),
                        Bool(d, "requiresAdministrator")));

        return new SetupManifest(
            ManifestVersion: (int)Sayi(root, "manifestVersion"),
            Application: PaketOku(app),
            Requirements: reqs.Count > 0 ? reqs : DefaultRequirements,
            Dependencies: deps);
    }

    private static JsonElement OkuKok(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "null")
            throw new SetupVerificationException("SURUM_YOK",
                "Sunucuda kurulum paketi bulunamadı (yönetici henüz sürüm yayınlamamış).");
        try
        {
            // NOT: JsonDocument bir kez okunur; Clone() ile belge kapansa da eleman geçerli kalır.
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            throw new SetupVerificationException("MANIFEST_BOZUK",
                "Sunucudan gelen kurulum bilgisi okunamadı. Kurulum iptal edildi.");
        }
    }

    private static SetupPackageInfo PaketOku(JsonElement e)
    {
        var version = Metin(e, "version");
        if (string.IsNullOrWhiteSpace(version))
            throw new SetupVerificationException("SURUM_YOK",
                "Sunucuda kurulum paketi bulunamadı (yönetici henüz sürüm yayınlamamış).");

        var url = Metin(e, "downloadUrl") ?? Metin(e, "url");
        return new SetupPackageInfo(
            version!,
            url ?? "",
            Metin(e, "checksum") ?? Metin(e, "sha256") ?? Metin(e, "checksumSha256"),
            Sayi(e, "sizeBytes"),
            Metin(e, "minSupportedVersion") ?? "0.0.0");
    }

    private static string? Metin(JsonElement e, string ad)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ad, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static long Sayi(JsonElement e, string ad)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ad, out var v)
           && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0L;

    private static bool Bool(JsonElement e, string ad)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(ad, out var v) && v.ValueKind == JsonValueKind.True;
}
