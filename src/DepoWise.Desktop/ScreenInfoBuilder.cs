using System;
using System.IO;
using System.Text;

namespace DepoWise.Desktop;

/// <summary>
/// Aktif ekranın gerçek tanımlayıcılarını ve (varsa) kaynak kodunu derler — kullanıcı bir alanı/tabloyu
/// gerçek View/ViewModel koduyla tarif edebilsin diye. Kaynak dosyalar geliştirme makinesinde okunur;
/// dağıtımda bulunamazsa yalnız tanımlayıcılar gösterilir.
/// </summary>
public static class ScreenInfoBuilder
{
    public static (string Title, string Body) Build(object? vm, string navKey, string screenTitle)
    {
        var vmType = vm?.GetType();
        var vmName = vmType?.Name ?? "—";
        var baseName = vmName.EndsWith("ViewModel", StringComparison.Ordinal)
            ? vmName[..^"ViewModel".Length] : vmName;
        var viewName = baseName + "View";

        var root = ProjectDir();
        var vmFile = root is null ? null : Path.Combine(root, "ViewModels", vmName + ".cs");
        var viewFile = root is null ? null : Path.Combine(root, "Views", viewName + ".axaml");

        var sb = new StringBuilder();
        sb.AppendLine($"EKRAN          : {screenTitle}");
        sb.AppendLine($"Nav anahtarı   : {navKey}");
        sb.AppendLine($"ViewModel      : {vmType?.FullName}");
        sb.AppendLine($"View           : DepoWise.Desktop.Views.{viewName}");
        sb.AppendLine($"VM dosyası     : {Rel(root, vmFile)}");
        sb.AppendLine($"View dosyası   : {Rel(root, viewFile)}");
        sb.AppendLine();
        sb.AppendLine("════════════════════ VIEW (XAML) ════════════════════");
        sb.AppendLine(ReadOr(viewFile));
        sb.AppendLine();
        sb.AppendLine("════════════════════ VIEWMODEL (C#) ════════════════════");
        sb.AppendLine(ReadOr(vmFile));

        return ($"Ekran Bilgisi — {screenTitle}", sb.ToString());
    }

    private static string? ProjectDir()
    {
        try
        {
            var loc = typeof(ScreenInfoBuilder).Assembly.Location;
            var dir = Path.GetDirectoryName(loc);            // ...\bin\Debug\net8.0
            for (int i = 0; i < 3 && dir is not null; i++) dir = Path.GetDirectoryName(dir);
            return dir is not null && Directory.Exists(Path.Combine(dir, "Views")) ? dir : null;
        }
        catch { return null; }
    }

    private static string Rel(string? root, string? file)
        => file is null ? "(kaynak yolu çözülemedi)"
         : root is null ? file
         : Path.GetRelativePath(root, file);

    private static string ReadOr(string? file)
    {
        try { return file is not null && File.Exists(file) ? File.ReadAllText(file) : "(kaynak bulunamadı — yalnız tanımlayıcılar)"; }
        catch (Exception ex) { return "(kaynak okunamadı: " + ex.Message + ")"; }
    }
}
