using System.Data.Common;
using DepoWise.Infrastructure.Database;

namespace DepoWise.Infrastructure.Materials;

/// <summary>
/// STK-10b-4 (2026-08-12) — STOK HAREKETİ FİLTRELERİNİN <b>TEK</b> SQL KAYNAĞI.
///
/// <b>Neden var.</b> Aynı defter iki yerden sorgulanıyor: <c>ReportService.StockMovements</c>
/// (Stok Hareketleri RAPORU + XLSX) ve <c>StockService.SearchMovements</c> (Stok Hareketleri
/// EKRANI, web + masaüstü). Filtre mantığı iki yerde ayrı yazılsaydı ekran ile rapor sessizce
/// farklı sonuç verebilirdi — STK-10b-4'ün kapattığı risk tam olarak budur. Bu sınıf yüzünden
/// ikinci bir hareket sorgulama mimarisi YOKTUR: <b>tek</b> WHERE üreteci vardır.
///
/// <b>🔴 B-1 (kapatılan hata).</b> Web ekranı lokasyon süzmesini, sunucudan gelen LİMİTLİ liste
/// üzerinde İSTEMCİDE yapıyordu: seçilen depoya ait hareket ilk N kaydın dışındaysa kullanıcı
/// onu HİÇ göremiyordu ve eksikliği fark edemiyordu. Filtre artık burada, yani <b>SQL'de</b>
/// ve <b>LIMIT'ten ÖNCE</b> uygulanıyor.
///
/// <b>Sıra (her iki çağıranda da aynı):</b>
/// <c>WHERE firma AND BranchScope(kapsam) AND tarih AND [lokasyon · tür · arama · malzeme]
/// ORDER BY created_at DESC, tie DESC LIMIT n</c>.
/// Kapsam DIŞ SINIRDIR; buradaki filtrelerin hepsi <c>AND</c> ile yalnız DARALTIR — hiçbiri
/// <c>OR</c> ile yetki sınırını genişletemez.
///
/// <b>Beklenen tablo takma adları:</b> <c>sm</c> = stock_movements · <c>m</c> = materials ·
/// <c>d</c> = stock_documents. İki çağıran da bu adları kullanır.
/// </summary>
public sealed class StockMovementFilterSql
{
    private readonly string[] _locations;      // gerçek depo kimlikleri
    private readonly bool _unassignedWanted;   // "" seçildi mi (📦 Atanmamış)
    private readonly string[] _types;
    private readonly string[] _materials;
    private readonly string? _search;

    /// <summary>WHERE'e eklenecek parça (başında " AND ..." ile gelir; filtre yoksa boş metin).</summary>
    public string Sql { get; }

    private StockMovementFilterSql(string[] locations, bool unassignedWanted, string[] types,
                                   string[] materials, string? search, string sql)
    {
        _locations = locations; _unassignedWanted = unassignedWanted;
        _types = types; _materials = materials; _search = search; Sql = sql;
    }

    /// <summary>Filtre parçasını kurar. Boş/null her alan = O FİLTRE YOK (mevcut davranış).</summary>
    /// <param name="locationIds">STK-06 lokasyon. Boş liste/null = 🌐 Tüm Şubeler (Atanmamış dahil hepsi).
    /// Boş METİN ("") = 📦 Atanmamış (lokasyonu bilinmeyen hareket) — gerçek bir depo DEĞİLDİR.</param>
    /// <param name="movementTypes">STK-10b-1. KANONİK <c>movement_type</c> anahtarları (etiket değil).
    /// Bilinmeyen anahtar sessizce eşleşmez → fail-closed ("hepsi" anlamına GELMEZ).</param>
    /// <param name="search">STK-10b-2 (ADR-104). Kod · ad · hareket notu · fatura no · belge no.
    /// ⚠️ <c>stock_documents.note</c> BİLİNÇLİ olarak kapsam dışıdır (STK-B2 karar bekliyor).</param>
    /// <param name="materialIds">STK-10b-3. <c>materials.id</c>. Yabancı firmanın kimliği eşleşmez
    /// (sorgu zaten <c>company_id</c>'ye kilitli) → fail-closed.</param>
    public static StockMovementFilterSql Build(
        IReadOnlyList<string>? locationIds,
        IReadOnlyList<string>? movementTypes,
        string? search,
        IReadOnlyList<string>? materialIds)
    {
        // ── Lokasyon (STK-06 semantiği) ──
        // Gerçek depo X → hareket X'i İLGİLENDİRİYORSA görünür: branch_id=X VEYA branch_from_id=X
        //   (transfer A→B: çıkış bacağı branch_id=A, giriş bacağı branch_from_id=A → ikisi de A'da görünür).
        var locs = locationIds is null ? Array.Empty<string>() : locationIds.Where(x => x is not null).Distinct().ToArray();
        var gercekDepolar = locs.Where(x => x.Length > 0).ToArray();
        var atanmamisIstendi = locs.Any(x => x.Length == 0);

        var sql = "";
        if (locs.Length > 0)
        {
            var parcalar = new List<string>();
            if (gercekDepolar.Length > 0)
            {
                var ps = string.Join(",", Enumerable.Range(0, gercekDepolar.Length).Select(i => "@loc" + i));
                parcalar.Add($"sm.branch_id IN ({ps})");
                parcalar.Add($"sm.branch_from_id IN ({ps})");
            }
            if (atanmamisIstendi)
                parcalar.Add("((sm.branch_id IS NULL OR sm.branch_id = '') AND (sm.branch_from_id IS NULL OR sm.branch_from_id = ''))");
            // ⚠️ Dallar TEK parantezde toplanır ve dışarıya AND ile bağlanır → kapsamı genişletemez.
            sql += " AND (" + string.Join(" OR ", parcalar) + ")";
        }

        // ── Hareket türü (STK-10b-1) ──
        var types = movementTypes is null
            ? Array.Empty<string>()
            : movementTypes.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (types.Length > 0)
            sql += $" AND sm.movement_type IN ({string.Join(",", Enumerable.Range(0, types.Length).Select(i => "@mtype" + i))})";

        // ── Serbest metin arama (STK-10b-2) — semantik mevcut ekrandan BİREBİR ──
        var q = string.IsNullOrWhiteSpace(search) ? null : search!.Trim();
        if (q is not null)
            sql += " AND (m.code LIKE @q OR m.name LIKE @q OR sm.note LIKE @q OR d.invoice_no LIKE @q OR d.doc_no LIKE @q)";

        // ── Malzeme (STK-10b-3) ──
        var mats = materialIds is null
            ? Array.Empty<string>()
            : materialIds.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        if (mats.Length > 0)
            sql += $" AND sm.material_id IN ({string.Join(",", Enumerable.Range(0, mats.Length).Select(i => "@mat" + i))})";

        return new StockMovementFilterSql(gercekDepolar, atanmamisIstendi, types, mats, q, sql);
    }

    /// <summary>Parametreleri bağlar. <see cref="Build"/> ile AYNI kaynaktan üretilir (deterministik).</summary>
    public void Bind(DbCommand cmd)
    {
        for (int i = 0; i < _locations.Length; i++) cmd.AddWithValue("@loc" + i, _locations[i]);
        for (int i = 0; i < _types.Length; i++) cmd.AddWithValue("@mtype" + i, _types[i]);
        if (_search is not null) cmd.AddWithValue("@q", "%" + _search + "%");
        for (int i = 0; i < _materials.Length; i++) cmd.AddWithValue("@mat" + i, _materials[i]);
        _ = _unassignedWanted;   // yalnız SQL'i etkiler; bağlanacak parametresi yoktur
    }
}
