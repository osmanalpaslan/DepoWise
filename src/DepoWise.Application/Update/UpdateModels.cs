namespace DepoWise.Application.Update;

/// <summary>Basit SemVer (X.Y.Z) karşılaştırma. Web `semver.ts` ile aynı.</summary>
public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public static bool TryParse(string? s, out SemVer v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Trim().TrimStart('v', 'V').Split('.');
        if (parts.Length != 3) return false;
        if (!int.TryParse(parts[0], out var a) || !int.TryParse(parts[1], out var b) || !int.TryParse(parts[2], out var c))
            return false;
        if (a < 0 || b < 0 || c < 0) return false;
        v = new SemVer(a, b, c);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";
}

/// <summary>Yayınlanan güncelleme paketi (web yönetiminden). checksum = SHA-256 hex.</summary>
public sealed record UpdatePackage(
    string Version, string ChecksumSha256, long SizeBytes, string MinSupportedVersion,
    string? ReleaseNotes, bool Signed, string? DownloadUrl = null);

public sealed record UpdateCheckResult(
    bool UpdateAvailable, string CurrentVersion, string? LatestVersion,
    bool BelowMinSupported, bool SignedWarning);
