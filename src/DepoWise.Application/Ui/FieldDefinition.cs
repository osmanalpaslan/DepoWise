using DepoWise.Application.Security;

namespace DepoWise.Application.Ui;

public enum FieldType { Text, Numeric, Date, Lookup, MultiSelect, Photo }

/// <summary>
/// Dinamik alan tanımı (Tanımlar / Alan Ayarları). Lookup/çoklu seçim/fotoğraf/+ butonu
/// özellikleri ve görünürlük/ekleme yetkisi buradan yönetilir. Ekrana sabit yazılmaz.
/// </summary>
public sealed record FieldDefinition(
    string Key,
    string Label,
    FieldType Type,
    string ModuleKey,                 // görünürlük bu modülün izinlerine bağlı
    bool IsLookup = false,
    bool AllowMultiSelect = false,
    bool HasPhoto = false,
    bool AllowAdd = false,            // "+" ile yeni lookup ekleme isteniyor mu
    bool Required = false,
    decimal? Min = null,
    decimal? Max = null,
    bool AllowNegative = false);

/// <summary>Alan + "+" butonu görünürlüğü — deny-by-default, permission'a bağlı (UI=API).</summary>
public static class FieldVisibility
{
    /// <summary>Alan görünür mü? Modülde okuma yetkisi yeterli.</summary>
    public static bool IsVisible(SessionContext s, FieldDefinition f)
        => AccessControl.Can(s, f.ModuleKey, PermissionAction.View);

    /// <summary>Alan düzenlenebilir mi? (yeni kayıt yaz / düzenle).</summary>
    public static bool IsEditable(SessionContext s, FieldDefinition f)
        => AccessControl.Can(s, f.ModuleKey, PermissionAction.Create)
           || AccessControl.Can(s, f.ModuleKey, PermissionAction.Edit);

    /// <summary>"+" yeni lookup ekleme butonu görünür mü? AllowAdd + yazma yetkisi şart.</summary>
    public static bool CanShowAddButton(SessionContext s, FieldDefinition f)
        => f.AllowAdd && AccessControl.Can(s, f.ModuleKey, PermissionAction.Create);

    /// <summary>Bir alan değeri için temel doğrulama (tür + zorunluluk + sınır).</summary>
    public static ValidationResult ValidateValue(FieldDefinition f, string? raw, decimal? numeric)
    {
        if (f.Required && string.IsNullOrWhiteSpace(raw) && numeric is null)
            return ValidationResult.Fail($"{f.Label} zorunlu.");
        return f.Type switch
        {
            FieldType.Date when !string.IsNullOrWhiteSpace(raw) => DateInput.Validate(raw),
            FieldType.Numeric when numeric is not null || f.Required
                => NumericInput.Validate(numeric, f.Min, f.Max, f.AllowNegative),
            _ => ValidationResult.Success,
        };
    }
}
