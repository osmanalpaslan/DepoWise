using Avalonia.Data.Converters;

namespace DepoWise.Desktop;

/// <summary>XAML'de kullanılan küçük dönüştürücüler (yalnız görsel; iş kuralı içermez).</summary>
public static class Conv
{
    /// <summary>bool → opaklık (true=1, false=0). Menü alt-listesinin açılışta yumuşak belirmesi için
    /// (ItemsControl.Opacity buna bağlanır + Opacity geçişi tanımlıdır).</summary>
    public static readonly IValueConverter BoolToOpacity =
        new FuncValueConverter<bool, double>(b => b ? 1d : 0d);
}
