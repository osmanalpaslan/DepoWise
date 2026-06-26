using System.Text;

namespace DepoWise.Application.Common;

/// <summary>
/// Keyset pagination için opak imleç. Kararlı/benzersiz sıralama (created_at, id) çiftini
/// base64url ile kodlar. İmleç istemciye opak; iç biçim değişebilir.
/// </summary>
public readonly record struct Cursor(long CreatedAt, string Id)
{
    public string Encode()
    {
        var raw = $"{CreatedAt}|{Id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string? value, out Cursor cursor)
    {
        cursor = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var s = value.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4) { case 2: s += "=="; break; case 3: s += "="; break; }
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(s));
            var sep = raw.IndexOf('|');
            if (sep <= 0) return false;
            if (!long.TryParse(raw[..sep], out var ts)) return false;
            cursor = new Cursor(ts, raw[(sep + 1)..]);
            return true;
        }
        catch { return false; }
    }
}
