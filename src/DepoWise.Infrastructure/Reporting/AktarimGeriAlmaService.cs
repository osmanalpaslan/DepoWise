using System.Data.Common;
using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Maintenance;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Operations;
using DepoWise.Infrastructure.Org;
using DepoWise.Infrastructure.Vehicles;

namespace DepoWise.Infrastructure.Reporting;

/// <summary>Geri alma sonucu. <c>Birakilanlar</c>: geri alınamayan kayıtlar ve nedeni (kullanıcıya gösterilir).</summary>
public sealed record AktarimGeriAlmaSonucu(int GeriAlinan, int ZatenGeriAlinmis, int TanimBirakilan, IReadOnlyList<string> Birakilanlar);

/// <summary>
/// ⭐ M-14 (AR-GE 2026-10-18, 05 F-063) — EXCEL İÇE AKTARIMINI GERİ ALMA.
///
/// Partide (<see cref="AktarimPartisi"/>) OLUŞAN kayıtlar en yeniden eskiye doğru, her biri KENDİ servisinin silme/iptal
/// kuralıyla geri alınır — hiçbir kural atlanmaz: kullanılmış malzeme silinmez (MLZ-01, stok defteri değişmez), depo
/// bakiyesini eksiye düşürecek yakıt girişi iptal edilmez, bakım iptali kendi stok/sayaç geri alımını yapar. Geri alınamayan
/// kayıt BIRAKILIR ve nedeni raporlanır (aktarımın geri kalanı yine geri alınır). Otomatik oluşan tanımlar (birim, marka,
/// kategori…) bırakılır: başka kayıtlar da kullanıyor olabilir. Tekrar çalıştırılabilir: zaten geri alınmış kayıt atlanır.
/// </summary>
public sealed class AktarimGeriAlmaService
{
    public const string Gerekce = "Excel içe aktarımı geri alındı";

    private readonly IDbConnectionFactory _factory;
    private readonly MaterialService _materials;
    private readonly VehicleService _vehicles;
    private readonly PersonnelService _personnel;
    private readonly FuelService _fuel;
    private readonly MaintenanceService _maintenance;
    private readonly InspectionService _inspections;
    private readonly IClock _clock;

    public AktarimGeriAlmaService(IDbConnectionFactory factory, MaterialService materials, VehicleService vehicles,
        PersonnelService personnel, FuelService fuel, MaintenanceService maintenance, InspectionService inspections,
        IClock? clock = null)
    {
        _factory = factory; _materials = materials; _vehicles = vehicles; _personnel = personnel;
        _fuel = fuel; _maintenance = maintenance; _inspections = inspections; _clock = clock ?? new SystemClock();
    }

    /// <summary>Tür → (tablo, "geri alınmış" kolonu, okunur ad). Listede olmayan türler tanım/bağlı kayıttır.</summary>
    private static readonly Dictionary<string, (string Tablo, string Kolon, string Ad)> Turler = new(StringComparer.Ordinal)
    {
        ["material"] = ("materials", "is_deleted", "Malzeme"),
        ["vehicle"] = ("vehicles", "is_deleted", "Araç"),
        ["personnel"] = ("personnel", "is_deleted", "Personel"),
        ["fuel_depot_entry"] = ("fuel_depot_entries", "is_deleted", "Yakıt depo girişi"),
        ["fuel_distribution"] = ("fuel_distributions", "is_deleted", "Yakıt dağıtımı"),
        ["vehicle_maintenance"] = ("vehicle_maintenances", "is_cancelled", "Bakım"),
        ["vehicle_inspection"] = ("vehicle_inspections", "is_deleted", "Muayene / sigorta"),
    };

    /// <summary>Ana kaydın parçası olan, onunla birlikte geri alınan türler (ayrıca sayılmaz).</summary>
    private static readonly HashSet<string> Bagli = new(StringComparer.Ordinal) { "stock_movement", "stock_document" };

    public AktarimGeriAlmaSonucu GeriAl(SessionContext s, string partiKimligi)
    {
        AccessControl.Require(s, "import_export", PermissionAction.View);
        if (string.IsNullOrWhiteSpace(partiKimligi) || !partiKimligi.StartsWith(AktarimPartisi.Onek, StringComparison.Ordinal))
            throw new ArgumentException("Geçersiz aktarım kimliği.");

        var kayitlar = new List<(string Tur, string Id)>();
        using (var conn = _factory.Create())
        using (var cmd = conn.CreateCommand())
        {
            // Yalnız bu firmanın, bu partide OLUŞTURULAN kayıtları (firma oturumdan; başka firmanın partisi görünmez).
            cmd.CommandText = "SELECT entity_type, entity_id FROM audit_logs WHERE company_id=@c AND correlation_id=@p AND action=@a " +
                              "ORDER BY created_at DESC;";
            cmd.AddWithValue("@c", s.CompanyId);
            cmd.AddWithValue("@p", partiKimligi);
            cmd.AddWithValue("@a", AuditActions.Create);
            using var r = cmd.ExecuteReader();
            var gorulen = new HashSet<string>(StringComparer.Ordinal);
            while (r.Read())
            {
                var tur = r.GetString(0); var id = r.GetString(1);
                if (gorulen.Add(tur + "|" + id)) kayitlar.Add((tur, id));
            }
        }
        if (kayitlar.Count == 0) throw new ForbiddenException("Aktarım bulunamadı (ya da bu firmaya ait değil).");

        int geri = 0, zaten = 0, tanim = 0;
        var birakilan = new List<string>();
        foreach (var (tur, id) in kayitlar)
        {
            if (Bagli.Contains(tur)) continue;
            if (!Turler.TryGetValue(tur, out var t)) { tanim++; continue; }
            if (ZatenGeriAlinmis(s.CompanyId, t.Tablo, t.Kolon, id)) { zaten++; continue; }
            try
            {
                switch (tur)
                {
                    case "material": _materials.Delete(s, id); break;
                    case "vehicle": _vehicles.Delete(s, id); break;
                    case "personnel": _personnel.SoftDelete(s, id); break;
                    case "fuel_depot_entry": _fuel.CancelDepotEntry(s, id, Gerekce); break;
                    case "fuel_distribution": _fuel.CancelDistribution(s, id, Gerekce); break;
                    case "vehicle_maintenance": _maintenance.Cancel(s, id, Gerekce); break;
                    case "vehicle_inspection": _inspections.Cancel(s, id, Gerekce); break;
                }
                geri++;
            }
            catch (Exception ex)
            {
                // İş kuralı mesajları kullanıcı metnidir; beklenmeyen hatanın ham metni (SQL/dosya yolu) gösterilmez.
                var neden = ex is ForbiddenException or InvalidOperationException or ArgumentException or ConcurrencyException
                    ? ex.Message : "beklenmeyen bir hata oluştu, kayıt bırakıldı";
                if (birakilan.Count < 50) birakilan.Add($"{t.Ad} ({Etiket(s.CompanyId, t.Tablo, id)}): {neden}");
            }
        }

        using (var conn = _factory.Create())
        using (var tx = conn.BeginTransaction())
        {
            AuditWriter.Write(conn, tx, new AuditEntry(s.CompanyId, "import_batch", partiKimligi, AuditActions.Reverse, s.UserId,
                AfterJson: $"{{\"geriAlinan\":{geri},\"zaten\":{zaten},\"birakilan\":{kayitlar.Count(k => Turler.ContainsKey(k.Tur)) - geri - zaten}}}"), _clock);
            tx.Commit();
        }
        return new AktarimGeriAlmaSonucu(geri, zaten, tanim, birakilan);
    }

    private bool ZatenGeriAlinmis(string companyId, string tablo, string kolon, string id)
    {
        using var conn = _factory.Create();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {kolon} FROM {tablo} WHERE id=@id AND company_id=@c;";
        cmd.AddWithValue("@id", id);
        cmd.AddWithValue("@c", companyId);
        var v = cmd.ExecuteScalar();
        return v is null or DBNull || Convert.ToInt64(v) == 1;   // kayıt yok (başka firma/silinmiş) da geri alınmış sayılır
    }

    /// <summary>Kullanıcıya gösterilecek kısa etiket (kod/plaka/ad); okunamazsa kimlik.</summary>
    private string Etiket(string companyId, string tablo, string id)
    {
        var kolon = tablo switch
        {
            "materials" => "code", "vehicles" => "COALESCE(plate, internal_code)", "personnel" => "full_name", _ => null,
        };
        if (kolon is null) return id;
        try
        {
            using var conn = _factory.Create();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {kolon} FROM {tablo} WHERE id=@id AND company_id=@c;";
            cmd.AddWithValue("@id", id);
            cmd.AddWithValue("@c", companyId);
            return cmd.ExecuteScalar() as string ?? id;
        }
        catch { return id; }
    }
}
