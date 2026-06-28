using System.Collections.Generic;
using System.Globalization;

namespace DepoWise.Desktop;

/// <summary>Düzenleme kaydetme onayında "değişen alan: yeni değer" listesi üretir.</summary>
public sealed class ChangeSummary
{
    private readonly List<string> _lines = new();

    public void Add(string label, object? oldVal, object? newVal)
    {
        var o = Norm(oldVal);
        var n = Norm(newVal);
        if (o != n) _lines.Add($"• {label}: {(string.IsNullOrEmpty(n) ? "—" : n)}");
    }

    public bool HasChanges => _lines.Count > 0;

    /// <summary>Onay mesajı: başlık + değişen alanlar (yoksa "değişiklik yok").</summary>
    public string Build(string header)
        => _lines.Count == 0
            ? header + "\n\n(Değişiklik yapılmadı.)"
            : header + "\n\nDeğişen alanlar:\n" + string.Join("\n", _lines);

    private static string Norm(object? v) => v switch
    {
        null => "",
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => (v.ToString() ?? "").Trim(),
    };
}
