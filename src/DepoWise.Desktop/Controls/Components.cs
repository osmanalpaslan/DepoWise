using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace DepoWise.Desktop.Controls;

// DepoWise ortak UI bileşenleri (Faz 5).
// KURAL: Bu dosyada YALNIZCA sunum amaçlı StyledProperty tanımları + pseudo-class güncellemesi vardır.
// İş mantığı YOKTUR (MVVM korunur). Görseller ControlTheme'lerden (Themes/ComponentThemes.axaml) gelir.

public enum BadgeKind { Neutral, Success, Warning, Danger, Info }
public enum StateMode { Empty, Error, Loading }

/// <summary>Durum rozeti / chip: Success/Warning/Danger/Info/Neutral.</summary>
public class StatusBadge : TemplatedControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<StatusBadge, string?>(nameof(Text));
    public static readonly StyledProperty<BadgeKind> KindProperty =
        AvaloniaProperty.Register<StatusBadge, BadgeKind>(nameof(Kind), BadgeKind.Neutral);

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public BadgeKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public StatusBadge() => UpdatePseudo();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == KindProperty) UpdatePseudo();
    }

    private void UpdatePseudo()
    {
        PseudoClasses.Set(":neutral", Kind == BadgeKind.Neutral);
        PseudoClasses.Set(":success", Kind == BadgeKind.Success);
        PseudoClasses.Set(":warning", Kind == BadgeKind.Warning);
        PseudoClasses.Set(":danger", Kind == BadgeKind.Danger);
        PseudoClasses.Set(":info", Kind == BadgeKind.Info);
    }
}

/// <summary>Form alanı sarmalayıcı: etiket + zorunlu işareti + yardım/hata metni + içerik (giriş kontrolü).</summary>
public class FormField : ContentControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(Label));
    public static readonly StyledProperty<bool> IsRequiredProperty =
        AvaloniaProperty.Register<FormField, bool>(nameof(IsRequired));
    public static readonly StyledProperty<string?> HelpTextProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(HelpText));
    public static readonly StyledProperty<string?> ErrorTextProperty =
        AvaloniaProperty.Register<FormField, string?>(nameof(ErrorText));
    public static readonly StyledProperty<bool> HasErrorProperty =
        AvaloniaProperty.Register<FormField, bool>(nameof(HasError));

    public string? Label { get => GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public bool IsRequired { get => GetValue(IsRequiredProperty); set => SetValue(IsRequiredProperty, value); }
    public string? HelpText { get => GetValue(HelpTextProperty); set => SetValue(HelpTextProperty, value); }
    public string? ErrorText { get => GetValue(ErrorTextProperty); set => SetValue(ErrorTextProperty, value); }
    public bool HasError { get => GetValue(HasErrorProperty); set => SetValue(HasErrorProperty, value); }

    public FormField() => UpdatePseudo();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == HasErrorProperty) UpdatePseudo();
    }

    private void UpdatePseudo() => PseudoClasses.Set(":error", HasError);
}

/// <summary>Bölüm başlığı: başlık + alt başlık + sağda aksiyon slotu.</summary>
public class SectionHeader : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<SectionHeader, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SubtitleProperty =
        AvaloniaProperty.Register<SectionHeader, string?>(nameof(Subtitle));
    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<SectionHeader, object?>(nameof(Actions));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Subtitle { get => GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? Actions { get => GetValue(ActionsProperty); set => SetValue(ActionsProperty, value); }
}

/// <summary>Modül araç çubuğu: başlık + arama + filtre slotu + birincil "Yeni Ekle" aksiyonu.</summary>
public class Toolbar : TemplatedControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<Toolbar, string?>(nameof(Title));
    public static readonly StyledProperty<string?> SearchTextProperty =
        AvaloniaProperty.Register<Toolbar, string?>(nameof(SearchText), defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);
    public static readonly StyledProperty<string?> SearchWatermarkProperty =
        AvaloniaProperty.Register<Toolbar, string?>(nameof(SearchWatermark), "Ara...");
    public static readonly StyledProperty<bool> ShowSearchProperty =
        AvaloniaProperty.Register<Toolbar, bool>(nameof(ShowSearch), true);
    public static readonly StyledProperty<object?> FilterContentProperty =
        AvaloniaProperty.Register<Toolbar, object?>(nameof(FilterContent));
    public static readonly StyledProperty<string?> PrimaryActionTextProperty =
        AvaloniaProperty.Register<Toolbar, string?>(nameof(PrimaryActionText));
    public static readonly StyledProperty<ICommand?> PrimaryActionCommandProperty =
        AvaloniaProperty.Register<Toolbar, ICommand?>(nameof(PrimaryActionCommand));

    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? SearchText { get => GetValue(SearchTextProperty); set => SetValue(SearchTextProperty, value); }
    public string? SearchWatermark { get => GetValue(SearchWatermarkProperty); set => SetValue(SearchWatermarkProperty, value); }
    public bool ShowSearch { get => GetValue(ShowSearchProperty); set => SetValue(ShowSearchProperty, value); }
    public object? FilterContent { get => GetValue(FilterContentProperty); set => SetValue(FilterContentProperty, value); }
    public string? PrimaryActionText { get => GetValue(PrimaryActionTextProperty); set => SetValue(PrimaryActionTextProperty, value); }
    public ICommand? PrimaryActionCommand { get => GetValue(PrimaryActionCommandProperty); set => SetValue(PrimaryActionCommandProperty, value); }
}

/// <summary>Boş veri / hata / yükleme için ortak durum paneli.</summary>
public class StatePanel : TemplatedControl
{
    public static readonly StyledProperty<StateMode> ModeProperty =
        AvaloniaProperty.Register<StatePanel, StateMode>(nameof(Mode), StateMode.Empty);
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<StatePanel, string?>(nameof(Title));
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<StatePanel, string?>(nameof(Message));
    public static readonly StyledProperty<string?> ActionTextProperty =
        AvaloniaProperty.Register<StatePanel, string?>(nameof(ActionText));
    public static readonly StyledProperty<ICommand?> ActionCommandProperty =
        AvaloniaProperty.Register<StatePanel, ICommand?>(nameof(ActionCommand));

    public StateMode Mode { get => GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public string? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string? Message { get => GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string? ActionText { get => GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }

    public StatePanel() => UpdatePseudo();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ModeProperty) UpdatePseudo();
    }

    private void UpdatePseudo()
    {
        PseudoClasses.Set(":empty", Mode == StateMode.Empty);
        PseudoClasses.Set(":error", Mode == StateMode.Error);
        PseudoClasses.Set(":loading", Mode == StateMode.Loading);
    }
}
