using System.Globalization;

namespace DepoWise.Application.Common;

/// <summary>
/// ═══ FAZ 4.3 — LOG SÖZLÜĞÜ (kullanıcı isteği 2026-09-06) ═══
///
/// <b>Kullanıcının şikâyeti.</b> "Log bilgileri anlaşılır değil… işlem tarihi, saati, yaptığı işlemi,
/// yapılan işlemin ÖNCEKİ ve SONRAKİ hâllerini … hangi alanda neyi güncelledi ise görebilmeliyim."
///
/// <b>Neden gerekliydi.</b> <c>audit_logs.before_json / after_json</c> sütunları şemada 001'den beri
/// VARDI ama 162 çağrı yerinin neredeyse tamamı bunları boş bırakıyordu → log yalnız "kim, ne zaman,
/// hangi tip, hangi işlem" diyordu; NE değiştiği hiç yazmıyordu. Bu sınıf + <c>AuditSnapshot</c> +
/// <c>AuditDiff</c> üçlüsü bu boşluğu kapatır.
///
/// <b>Değişmezler.</b>
/// <list type="bullet">
///   <item>Log satırı SİLİNMEZ/DEĞİŞMEZ — yalnız daha fazla bilgi yazılır.</item>
///   <item><see cref="Gizli"/> sütunlar loga ASLA yazılmaz (parola özeti, jeton, imza…). Güvenlik
///   sütununu loga düşürmek, logu okuyabilen herkese kimlik bilgisi vermek olurdu.</item>
///   <item>Teknik sütunlar (<c>id</c>, <c>version</c>, <c>updated_at</c>…) kullanıcıya GÖSTERİLMEZ:
///   her güncellemede değiştikleri için gerçek değişikliği gizlerler.</item>
/// </list>
/// </summary>
public static class AuditFields
{
    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 1) VARLIK TİPİ → TABLO  (anlık görüntü bu tablodan alınır)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// <c>audit_logs.entity_type</c> → gerçek tablo adı. Yalnız BURADAKİ tablolar sorgulanır:
    /// bilinmeyen tip için sorgu ÇALIŞTIRILMAZ (yanlış tablo adı PostgreSQL'de transaction'ı
    /// bozardı — beyaz liste bunu imkânsız kılar).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Tablolar =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user"] = "users",
            ["branch"] = "branches",
            ["project"] = "projects",
            ["company"] = "companies",
            ["personnel"] = "personnel",
            ["personnel_title"] = "personnel_titles",
            ["team"] = "teams",
            ["team_member"] = "team_members",
            ["material"] = "materials",
            ["material_template"] = "material_templates",
            ["material_request"] = "material_requests",
            ["vehicle"] = "vehicles",
            ["vehicle_template"] = "vehicle_templates",
            ["vehicle_maintenance"] = "vehicle_maintenances",
            ["vehicle_inspection"] = "vehicle_inspections",
            ["maintenance_definition"] = "maintenance_definitions",
            ["equipment"] = "equipment",
            ["equipment_maintenance"] = "equipment_maintenances",
            ["equipment_inspection"] = "equipment_inspections",
            ["fuel_depot_entry"] = "fuel_depot_entries",
            ["fuel_distribution"] = "fuel_distributions",
            ["daily_activity"] = "daily_activities",
            ["stock_document"] = "stock_documents",
            ["stock_movement"] = "stock_movements",
            ["assignment_movement"] = "assignment_movements",
            ["purchase_order"] = "purchase_orders",
            ["work_order"] = "work_orders",
            ["calendar_event"] = "calendar_events",
            ["announcement"] = "announcements",
            ["file_record"] = "file_records",
            ["cost_center"] = "cost_centers",
            ["cost_center_link"] = "cost_center_links",
            ["party"] = "parties",
            ["party_ledger"] = "party_ledger",
            ["invoices"] = "invoices",
            ["finance_accounts"] = "finance_accounts",
            ["finance_transactions"] = "finance_transactions",
            ["custom_report_def"] = "custom_report_defs",
            // Tanımlar ekranı tablo ADIYLA loglar (LookupService) → tip = tablo.
            ["units"] = "units",
            ["brands"] = "brands",
            ["suppliers"] = "suppliers",
            ["material_categories"] = "material_categories",
            ["vehicle_types"] = "vehicle_types",
            ["vehicle_categories"] = "vehicle_categories",
            ["vehicle_models"] = "vehicle_models",
        };

    /// <summary>Bu varlık tipinin anlık görüntüsü alınabilir mi; alınabiliyorsa tablo adı.</summary>
    public static string? Tablo(string? entityType)
        => entityType is not null && Tablolar.TryGetValue(entityType, out var t) ? t : null;

    /// <summary>Anlık görüntüsü alınabilen tüm varlık tipleri (test/denetim için).</summary>
    public static IReadOnlyCollection<string> AnlikGoruntuluTipler
        => (IReadOnlyCollection<string>)Tablolar.Keys;

    /// <summary>⭐ FAZ 4.4 — TERS ARAMA: tablo adından log varlık tipi ("vehicles" → "vehicle").
    /// Senkron çakışması tabloyla, log ise varlık tipiyle konuşur; bu köprü olmadan çakışma çözümü
    /// kaydın KENDİ geçmişine düşmez ve kullanıcı "bu değişikliği kim yaptı" sorusunu cevaplayamaz.</summary>
    public static string? TipTablodan(string? tablo)
    {
        if (string.IsNullOrWhiteSpace(tablo)) return null;
        foreach (var (tip, t) in Tablolar) if (string.Equals(t, tablo, StringComparison.Ordinal)) return tip;
        return null;
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 1b) BAĞLANTI SÜTUNU → HANGİ TABLONUN ADI GÖSTERİLECEK
    // ══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// ⭐ FAZ 4 FINAL QA (2026-09-06) — <b>KİMLİK YERİNE AD.</b>
    ///
    /// QA sırasında ölçüldü: kayıt logunda <c>Şube: — → 0a795b41…</c> yazıyordu. Kullanıcının isteği
    /// "hangi alanda NEYİ güncelledi ise görebilmeliyim" idi; 32 haneli bir kimlik bunu karşılamaz.
    /// Bu eşleme, bağlantı sütunlarının değerini ilgili tablodan OKUNUR ADA çevirir.
    ///
    /// Ad bulunamazsa ham değer olduğu gibi kalır — uydurma ad yazılmaz.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Tablo, string AdSutunu)> BagliTablolar =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["branch_id"] = ("branches", "name"),
            ["from_branch_id"] = ("branches", "name"),
            ["to_branch_id"] = ("branches", "name"),
            ["op_branch_id"] = ("branches", "name"),
            ["project_id"] = ("projects", "name"),
            ["personnel_id"] = ("personnel", "full_name"),
            ["driver_personnel_id"] = ("personnel", "full_name"),
            ["assignee_personnel_id"] = ("personnel", "full_name"),
            ["responsible_personnel_id"] = ("personnel", "full_name"),
            ["manager_personnel_id"] = ("personnel", "full_name"),
            ["technician_id"] = ("personnel", "full_name"),
            ["user_id"] = ("users", "username"),
            ["created_by"] = ("users", "username"),
            ["cancelled_by"] = ("users", "username"),
            ["approved_by"] = ("users", "username"),
            ["requested_by"] = ("users", "username"),
            ["material_id"] = ("materials", "name"),
            ["vehicle_id"] = ("vehicles", "internal_code"),
            ["equipment_id"] = ("equipment", "name"),
            ["unit_id"] = ("units", "name"),
            ["brand_id"] = ("brands", "name"),
            ["category_id"] = ("material_categories", "name"),
            ["supplier_id"] = ("suppliers", "name"),
            ["party_id"] = ("parties", "name"),
            ["team_id"] = ("teams", "name"),
            ["cost_center_id"] = ("cost_centers", "name"),
            ["vehicle_type_id"] = ("vehicle_types", "name"),
            ["vehicle_model_id"] = ("vehicle_models", "name"),
            ["definition_id"] = ("maintenance_definitions", "name"),
            ["maintenance_def_id"] = ("maintenance_definitions", "name"),
        };

    /// <summary>Bu sütun bir kayda bağlantı mı; öyleyse hangi tablonun hangi ad sütunu gösterilir.</summary>
    public static (string Tablo, string AdSutunu)? BagliTablo(string column)
        => BagliTablolar.TryGetValue(column, out var v) ? v : null;

    /// <summary>Ad çözümlemesi yapılacak tüm bağlantı sütunları (servis toplu sorgu için kullanır).</summary>
    public static IReadOnlyCollection<string> BagliSutunlar
        => (IReadOnlyCollection<string>)BagliTablolar.Keys;

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 2) LOGA HİÇ YAZILMAYACAK SÜTUNLAR  (güvenlik)
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> GizliSutunlar = new(StringComparer.Ordinal)
    {
        "password_hash", "special_code_hash", "enroll_key_hash", "key_hash", "token_hash",
        "payload_hash", "signature", "sha256", "permissions_json", "payload_json",
        "incoming_payload", "before_json", "after_json", "columns_json", "buttons_json",
        "filters_json", "national_id", "iban", "download_url",
    };

    /// <summary>Bu sütun loga YAZILMAZ (parola özeti, jeton, imza, kişisel kimlik…).</summary>
    public static bool Gizli(string column) => GizliSutunlar.Contains(column);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 3) KULLANICIYA GÖSTERİLMEYEN TEKNİK SÜTUNLAR
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static readonly HashSet<string> TeknikSutunlar = new(StringComparer.Ordinal)
    {
        "id", "company_id", "version", "created_at", "updated_at", "device_updated_at",
        "server_updated_at", "base_version", "operation_id", "stock_operation_id",
        "correlation_id", "device_id", "transfer_group_id", "group_key_override",
    };

    /// <summary>Teknik sütun: değeri her güncellemede değiştiği için farkı gizler → gösterilmez.</summary>
    public static bool Teknik(string column) => TeknikSutunlar.Contains(column);

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 4) SÜTUN → TÜRKÇE ETİKET
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static readonly IReadOnlyDictionary<string, string> Etiketler =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = "Ad", ["full_name"] = "Ad Soyad", ["username"] = "Kullanıcı Adı",
            ["code"] = "Kod", ["internal_code"] = "İç Kod", ["material_code"] = "Malzeme Kodu",
            ["material_name"] = "Malzeme Adı", ["description"] = "Açıklama", ["note"] = "Not",
            // ⭐ 2026-09-06: kullanıcı iletişim alanları (Migration095). "title" zaten "Başlık" olarak
            // eşleniyor; kullanıcı kaydında anlamı UNVAN olduğu için denetim kaydında karışmasın diye
            // ayrı bir anahtar YOKTUR — varlık türü zaten "user" olarak yazılır ve bağlam oradan okunur.
            ["notes"] = "Not",
            ["title"] = "Başlık", ["body"] = "İçerik", ["reason"] = "Gerekçe",
            ["status"] = "Durum", ["status_note"] = "Durum Notu", ["kind"] = "Tür",
            ["type"] = "Tür", ["direction"] = "Yön", ["result"] = "Sonuç",
            ["is_active"] = "Aktif", ["is_deleted"] = "Silindi", ["is_cancelled"] = "İptal",
            ["is_reversed"] = "Ters Kayıt", ["is_default"] = "Varsayılan", ["is_locked"] = "Kilitli",
            ["is_person"] = "Gerçek Kişi", ["is_field_staff"] = "Saha Personeli",
            ["quantity"] = "Miktar", ["old_quantity"] = "Eski Miktar", ["new_quantity"] = "Yeni Miktar",
            ["counted_qty"] = "Sayılan Miktar", ["system_qty"] = "Sistem Miktarı",
            ["diff_qty"] = "Fark", ["received_qty"] = "Teslim Alınan", ["min_stock"] = "Kritik Stok",
            ["unit_price"] = "Birim Fiyat", ["amount"] = "Tutar", ["line_total"] = "Satır Toplamı",
            ["subtotal"] = "Ara Toplam", ["grand_total"] = "Genel Toplam", ["net_total"] = "Net Toplam",
            ["vat_amount"] = "KDV Tutarı", ["vat_rate"] = "KDV Oranı", ["vat_total"] = "KDV Toplamı",
            ["discount_rate"] = "İskonto Oranı", ["discount_amount"] = "İskonto Tutarı",
            ["discount_total"] = "İskonto Toplamı", ["withholding_rate"] = "Tevkifat Oranı",
            ["withholding_amount"] = "Tevkifat Tutarı", ["withholding_total"] = "Tevkifat Toplamı",
            ["currency"] = "Para Birimi", ["currency_code"] = "Para Birimi", ["fx_rate"] = "Kur",
            ["rate"] = "Oran", ["rate_to_base"] = "Ana Para Birimi Kuru",
            ["plate"] = "Plaka", ["chassis_no"] = "Şasi No", ["engine_no"] = "Motor No",
            ["serial_no"] = "Seri No", ["production_year"] = "Model Yılı",
            ["current_meter"] = "Sayaç", ["prev_meter"] = "Önceki Sayaç",
            ["meter_unit"] = "Sayaç Birimi", ["default_meter_unit"] = "Sayaç Birimi",
            ["performed_km"] = "Yapıldığı Km", ["performed_hour"] = "Yapıldığı Saat",
            ["performed_date"] = "Yapıldığı Tarih", ["next_due_km"] = "Sonraki Km",
            ["next_due_hour"] = "Sonraki Saat", ["next_due_date"] = "Sonraki Tarih",
            ["next_date"] = "Sonraki Tarih", ["last_date"] = "Son Tarih",
            ["interval_value"] = "Periyot", ["interval_unit"] = "Periyot Birimi",
            ["liters"] = "Litre", ["location"] = "Konum", ["place"] = "Yer", ["address"] = "Adres",
            ["city"] = "İl", ["district"] = "İlçe", ["phone"] = "Telefon", ["email"] = "E-posta",
            ["tax_no"] = "Vergi No", ["tax_office"] = "Vergi Dairesi", ["account_no"] = "Hesap No",
            ["bank_name"] = "Banka", ["bank_branch"] = "Banka Şubesi",
            ["doc_no"] = "Belge No", ["doc_type"] = "Belge Türü", ["doc_date"] = "Belge Tarihi",
            ["invoice_no"] = "Fatura No", ["invoice_date"] = "Fatura Tarihi",
            ["order_no"] = "Sipariş No", ["order_date"] = "Sipariş Tarihi", ["wo_no"] = "Emir No",
            ["reference_no"] = "Referans No", ["external_no"] = "Dış Belge No",
            ["entry_date"] = "Giriş Tarihi", ["distribution_date"] = "Dağıtım Tarihi",
            ["activity_date"] = "Faaliyet Tarihi", ["activity_type"] = "Faaliyet Türü",
            ["request_date"] = "Talep Tarihi", ["txn_date"] = "İşlem Tarihi", ["txn_type"] = "İşlem Türü",
            ["start_date"] = "Başlangıç", ["end_date"] = "Bitiş", ["due_date"] = "Termin",
            ["valid_from"] = "Geçerlilik Başlangıcı", ["valid_until"] = "Geçerlilik Bitişi",
            ["planned_start"] = "Planlanan Başlangıç", ["planned_end"] = "Planlanan Bitiş",
            ["actual_start"] = "Gerçekleşen Başlangıç", ["actual_end"] = "Gerçekleşen Bitiş",
            ["publish_start"] = "Yayın Başlangıcı", ["publish_end"] = "Yayın Bitişi",
            ["duration_days"] = "Süre (gün)", ["priority"] = "Öncelik", ["importance"] = "Önem",
            ["sort_order"] = "Sıra", ["line_no"] = "Satır No", ["step_no"] = "Adım No",
            ["unit"] = "Birim", ["unit_id"] = "Birim", ["brand_id"] = "Marka",
            ["category_id"] = "Kategori", ["supplier_id"] = "Tedarikçi", ["party_id"] = "Cari",
            ["branch_id"] = "Şube", ["parent_id"] = "Üst Kayıt", ["project_id"] = "Proje",
            ["material_id"] = "Malzeme", ["vehicle_id"] = "Araç", ["equipment_id"] = "Ekipman",
            ["personnel_id"] = "Personel", ["user_id"] = "Kullanıcı", ["team_id"] = "Takım",
            ["template_id"] = "Şablon", ["definition_id"] = "Tanım", ["cost_center_id"] = "Masraf Merkezi",
            ["vehicle_type_id"] = "Araç Türü", ["vehicle_model_id"] = "Araç Modeli",
            ["driver_personnel_id"] = "Sürücü", ["technician_id"] = "Teknisyen",
            ["responsible_personnel_id"] = "Sorumlu", ["assignee_personnel_id"] = "Zimmetli",
            ["manager_personnel_id"] = "Yönetici", ["manager_user_id"] = "Yönetici",
            ["from_branch_id"] = "Çıkış Şubesi", ["to_branch_id"] = "Varış Şubesi",
            ["from_location_id"] = "Çıkış Konumu", ["to_location_id"] = "Varış Konumu",
            ["from_status"] = "Önceki Durum", ["to_status"] = "Yeni Durum",
            ["from_team_stock"] = "Takım Stoğundan", ["warehouse_id"] = "Depo",
            ["movement_type"] = "Hareket Türü", ["movement_kind"] = "Hareket Sınıfı",
            ["payment_method"] = "Ödeme Şekli", ["account_kind"] = "Hesap Türü",
            ["party_type"] = "Cari Türü", ["cancel_reason"] = "İptal Gerekçesi",
            ["reversal_reason"] = "Ters Kayıt Gerekçesi", ["closing_note"] = "Kapanış Notu",
            ["ops_note"] = "Operasyon Notu", ["operation_status"] = "Operasyon Durumu",
            ["mime"] = "Dosya Türü", ["size_bytes"] = "Boyut (bayt)", ["storage_key"] = "Dosya Anahtarı",
            ["created_by"] = "Oluşturan", ["cancelled_by"] = "İptal Eden", ["approved_by"] = "Onaylayan",
            ["requested_by"] = "Talep Eden", ["completed_by"] = "Tamamlayan", ["uploaded_by"] = "Yükleyen",
            ["cancelled_at"] = "İptal Zamanı", ["approved_at"] = "Onay Zamanı",
            ["requested_at"] = "Talep Zamanı", ["closed_at"] = "Kapanış Zamanı",
            ["started_at"] = "Başlama Zamanı", ["expires_at"] = "Geçerlilik Sonu",
            ["role_key"] = "Rol", ["module_key"] = "Modül", ["button_key"] = "Düğme",
            ["screen_key"] = "Ekran", ["field_key"] = "Alan", ["required"] = "Zorunlu",
            ["can_view"] = "Görebilir", ["can_create"] = "Ekleyebilir",
            ["can_edit"] = "Düzenleyebilir", ["can_delete"] = "Silebilir",
            ["can_view_all_branches"] = "Tüm Şubeleri Görür", ["scope_all"] = "Tüm Kapsam",
            ["must_change_password"] = "Parola Değiştirmeli", ["is_global"] = "Genel",
            ["sub_definition_note"] = "Alt Tanım Notu", ["sub_definition_id"] = "Alt Tanım",
            ["maintenance_def_id"] = "Bakım Tanımı", ["maintenance_id"] = "Bakım",
            ["work_order_id"] = "İş Emri", ["request_id"] = "Talep", ["invoice_id"] = "Fatura",
            ["order_id"] = "Sipariş", ["document_id"] = "Belge", ["source"] = "Kaynak",
            ["source_type"] = "Kaynak Türü", ["source_module"] = "Kaynak Ekran",
            ["warning_text"] = "Uyarı Metni", ["level"] = "Seviye",
            ["value"] = "Değer", ["setting_key"] = "Ayar", ["setting_value"] = "Değer",
        };

    /// <summary>Sütunun kullanıcıya gösterilecek Türkçe adı. Sözlükte yoksa okunur hâle getirilir
    /// (uydurma çeviri yapılmaz: <c>foo_bar</c> → "Foo Bar").</summary>
    public static string Etiket(string column)
    {
        if (Etiketler.TryGetValue(column, out var v)) return v;
        var parcalar = column.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parcalar.Select(p => p.Length == 0 ? p
            : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 4b) VARLIK TİPİ → TÜRKÇE AD  ("vehicle" yerine "Araç")
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static readonly IReadOnlyDictionary<string, string> TipAdlari =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["user"] = "Kullanıcı", ["branch"] = "Şube", ["project"] = "Proje", ["company"] = "Firma",
            ["personnel"] = "Personel", ["personnel_title"] = "Personel Ünvanı",
            ["team"] = "Takım", ["team_member"] = "Takım Üyesi",
            ["material"] = "Malzeme", ["material_template"] = "Malzeme Şablonu",
            ["material_request"] = "Malzeme Talebi",
            ["vehicle"] = "Araç", ["vehicle_template"] = "Araç Şablonu",
            ["vehicle_maintenance"] = "Araç Bakımı", ["vehicle_inspection"] = "Muayene/Sigorta",
            ["maintenance_definition"] = "Bakım Tanımı",
            ["equipment"] = "Ekipman", ["equipment_maintenance"] = "Ekipman Bakımı",
            ["equipment_inspection"] = "Ekipman Muayenesi",
            ["fuel_depot_entry"] = "Yakıt Depo Girişi", ["fuel_distribution"] = "Yakıt Dağıtımı",
            ["daily_activity"] = "Günlük Faaliyet",
            ["stock_document"] = "Stok Belgesi", ["stock_movement"] = "Stok Hareketi",
            ["assignment_movement"] = "Zimmet Hareketi",
            ["purchase_order"] = "Satın Alma Siparişi", ["work_order"] = "İş Emri",
            ["calendar_event"] = "Takvim Kaydı", ["announcement"] = "Duyuru",
            ["file_record"] = "Evrak/Fotoğraf",
            ["cost_center"] = "Masraf Merkezi", ["cost_center_link"] = "Masraf Merkezi Bağlantısı",
            ["party"] = "Cari", ["party_ledger"] = "Cari Hareketi", ["invoices"] = "Fatura",
            ["finance_accounts"] = "Kasa/Banka Hesabı", ["finance_transactions"] = "Kasa/Banka Hareketi",
            ["custom_report_def"] = "Özel Rapor Tanımı",
            ["units"] = "Birim Tanımı", ["brands"] = "Marka Tanımı", ["suppliers"] = "Tedarikçi Tanımı",
            ["material_categories"] = "Malzeme Kategorisi", ["vehicle_types"] = "Araç Türü Tanımı",
            ["vehicle_categories"] = "Araç Kategorisi", ["vehicle_models"] = "Araç Modeli Tanımı",
            ["user_permissions"] = "Kullanıcı Yetkisi", ["role_permissions"] = "Rol Yetkisi",
            ["user_scopes"] = "Kullanıcı Şube Kapsamı", ["user_hierarchy"] = "Kullanıcı Hiyerarşisi",
            ["user_view_all_branches"] = "Tüm Şube Yetkisi", ["company_permissions"] = "Firma Yetkisi",
            ["field_protections"] = "Alan Koruması", ["field_requirements"] = "Alan Zorunluluğu",
            ["menu_layout"] = "Menü Düzeni", ["screen_platform_visibility"] = "Ekran Görünürlüğü",
            ["approval_instance"] = "Onay Akışı", ["approval_step"] = "Onay Adımı",
            ["app_release"] = "Güncelleme Paketi", ["machine_reset"] = "Cihaz Sıfırlama",
            ["company_purge"] = "Firma Kalıcı Silme", ["company_business_reset"] = "Firma Verisi Sıfırlama",
            ["company_local_reset"] = "Yerel Sıfırlama",
            // FAZ 4 FINAL QA (2026-09-06): firma ayarı satırları logda ham anahtarla görünüyordu.
            ["app_setting"] = "Firma Ayarı", ["setting"] = "Firma Ayarı",
        };

    /// <summary>Log satırındaki varlık tipinin Türkçe adı. Bilinmiyorsa ham değer döner
    /// (uydurma isim yazmak, kullanıcıyı yanlış kayda yönlendirirdi).</summary>
    public static string TipEtiket(string? entityType)
        => entityType is not null && TipAdlari.TryGetValue(entityType, out var v) ? v : (entityType ?? "");

    // ══════════════════════════════════════════════════════════════════════════════════════════
    // 5) DEĞER BİÇİMLEME
    // ══════════════════════════════════════════════════════════════════════════════════════════

    private static bool ZamanSutunu(string c)
        => c.EndsWith("_at", StringComparison.Ordinal) || c is "snapshot_at";

    private static bool GunSutunu(string c)
        => c.EndsWith("_date", StringComparison.Ordinal) || c is "as_of" or "valid_from" or "valid_until";

    private static bool EvetHayirSutunu(string c)
        => c.StartsWith("is_", StringComparison.Ordinal) || c.StartsWith("can_", StringComparison.Ordinal)
           || c is "required" or "enabled" or "signed" or "valid" or "success" or "affects_stock"
                or "stock_processed" or "personnel_seen" or "scope_all" or "sort_desc"
                or "must_change_password" or "from_team_stock";

    /// <summary>Ham değeri kullanıcıya okunur hâle getirir (boş → "—", 0/1 → Hayır/Evet,
    /// Unix ms → tarih/saat). Bilinmeyen tür olduğu gibi bırakılır — uydurma yapılmaz.</summary>
    /// <summary>
    /// ⭐ FAZ 4 FINAL QA (2026-09-06) — teknik durum kodları için Türkçe karşılık.
    /// QA'da <c>Durum: — → active</c> görüldü; kullanıcı için "Aktif" olmalı. Yalnız TÜM ekranlarda
    /// aynı anlama gelen değerler çevrilir — ekrana özel durum sözlükleri buraya KONMAZ, aksi hâlde
    /// bir ekranın "closed" değeri başka ekranın anlamıyla yanlış çevrilirdi.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> OrtakDegerler =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["active"] = "Aktif", ["passive"] = "Pasif", ["inactive"] = "Pasif",
            ["km"] = "km", ["hour"] = "saat",
        };

    public static string Deger(string column, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "—";
        if (column is "status" or "meter_unit" or "default_meter_unit" or "interval_unit"
            && OrtakDegerler.TryGetValue(raw, out var ortak)) return ortak;
        if (EvetHayirSutunu(column))
            return raw is "1" or "true" or "True" ? "Evet"
                 : raw is "0" or "false" or "False" ? "Hayır" : raw;

        if ((ZamanSutunu(column) || GunSutunu(column))
            && long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms)
            && ms > 100_000_000_000L && ms < 4_000_000_000_000L)
        {
            var t = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
            return GunSutunu(column) && t.TimeOfDay == TimeSpan.Zero
                ? t.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
                : t.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);
        }
        return raw.Length > 200 ? raw[..200] + "…" : raw;
    }
}
