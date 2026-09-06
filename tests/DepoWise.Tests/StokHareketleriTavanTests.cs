using DepoWise.Application.Common;
using DepoWise.Application.Security;
using DepoWise.Infrastructure.Database;
using DepoWise.Infrastructure.Database.Migrations;
using DepoWise.Infrastructure.Materials;
using DepoWise.Infrastructure.Security;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ STOK HAREKETLERİ: SESSİZ KESME (10.000 kayıtlık yük testinde bulundu, 2026-09-06) ═══
///
/// <para><b>Bulunan hata:</b> Stok Hareketleri ekranı en fazla 1000 satır okur ve <b>okuduğu satır
/// sayısını</b> "N hareket" diye yazıyordu. 10.000 hareketi olan bir firmada ekran "1000 hareket"
/// diyor, kullanıcı toplamının bu olduğunu sanıyor ve kalan 9.000 kayıt sessizce düşüyordu.
/// Hata İKİ ORTAMDA da vardı (masaüstü ve web aynı ucu kullanıyor).</para>
///
/// <para><b>Düzeltme:</b> tavan korunur (10.000 satırı tek seferde çizmek ekranı kilitler) ama
/// GERÇEK toplam ayrıca sorulur ve tavana takıldığı kullanıcıya açıkça söylenir.</para>
/// </summary>
public class StokHareketleriTavanTests : IDisposable
{
    private const string Co = "TAVAN";
    private readonly string _dbPath;
    private readonly SqliteConnectionFactory _f;
    private readonly SessionContext _admin;
    private readonly string _mat, _sube;
    private const long Gun = 1_700_000_000_000;

    public StokHareketleriTavanTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), "dw_tavan_" + Guid.NewGuid().ToString("N") + ".db");
        _f = new SqliteConnectionFactory(_dbPath);
        new MigrationRunner(_f).Run();

        using (var conn = _f.Create())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = $"INSERT INTO companies(id,name,created_at,updated_at,version,is_deleted) VALUES('{Co}','{Co}',1,1,1,0);";
            cmd.ExecuteNonQuery();
        }
        var uid = new UserService(_f).EnsureInitialAdmin(Co, "admin", "admin123", RoleKeys.CompanyAdmin);
        _admin = new SessionContext(uid, Co, new[] { RoleKeys.CompanyAdmin }, PermissionSet.Empty);
        _sube = new DepoWise.Infrastructure.Organization.BranchService(_f)
            .Create(_admin, new DepoWise.Infrastructure.Organization.NewBranch("Merkez"));
        _mat = new MaterialService(_f).Create(_admin, new NewMaterial("M-1", "Çimento", UnitPrice: 10m));
    }

    /// <summary>Ham SQL ile hızlı hareket üretimi (tek transaction).</summary>
    private void HareketUret(int adet)
    {
        using var conn = _f.Create();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            "INSERT INTO stock_movements(id,company_id,material_id,branch_id,movement_type,direction," +
            "quantity,unit_price,currency_code,operation_id,note,created_at,updated_at) " +
            "VALUES(@id,@co,@mat,@br,'in',1,'1','10','TRY',@op,@note,@ts,@ts);";
        Microsoft.Data.Sqlite.SqliteParameter Ekle(string ad)
        {
            var x = (Microsoft.Data.Sqlite.SqliteParameter)cmd.CreateParameter();
            x.ParameterName = ad; cmd.Parameters.Add(x); return x;
        }
        var id = Ekle("@id"); var co = Ekle("@co"); var mat = Ekle("@mat"); var br = Ekle("@br");
        var op = Ekle("@op"); var note = Ekle("@note"); var ts = Ekle("@ts");
        co.Value = Co; mat.Value = _mat; br.Value = _sube;
        for (int i = 0; i < adet; i++)
        {
            id.Value = Guid.NewGuid().ToString("N");
            op.Value = "seed-" + i;
            note.Value = "Sevkiyat " + i;
            ts.Value = Gun + i;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    /// <summary>
    /// HATANIN KENDİSİ: liste ucu tavanda kesilir — dolayısıyla dönen satır sayısı TOPLAM DEĞİLDİR.
    /// Ekranlar bu sayıyı "toplam" diye göstermemelidir.
    /// </summary>
    [Fact]
    public void ListeUcu_TavandaKeser_DonenSayi_ToplamDegildir()
    {
        HareketUret(1200);
        var stok = new StockService(_f);

        var satirlar = stok.SearchMovements(_admin, null, null, null, null, null, null, 1000);
        Assert.Equal(1000, satirlar.Count);          // tavan uygulanıyor (bilinçli)

        var sayfa = stok.SearchMovementsGrid(_admin, null, null, null, null, null, null, page: 1, pageSize: 1);
        Assert.Equal(1200, sayfa.TotalCount);        // GERÇEK toplam buradan gelir
        Assert.True(sayfa.TotalCount > satirlar.Count,
            "Bu testin anlamı, toplamın dönen satır sayısından BÜYÜK olabilmesidir.");
    }

    /// <summary>Toplam, AYNI filtreye uymalı: filtre daraldıkça toplam da daralır (yanlış toplam gösterilmesin).</summary>
    [Fact]
    public void GercekToplam_AyniFiltreyi_Uygular()
    {
        HareketUret(1200);
        var stok = new StockService(_f);

        var hepsi = stok.SearchMovementsGrid(_admin, null, null, null, null, null, null, page: 1, pageSize: 1);
        Assert.Equal(1200, hepsi.TotalCount);

        // Tarih aralığı ile daralt: ilk 100 hareket.
        var daraltilmis = stok.SearchMovementsGrid(_admin, Gun, Gun + 99, null, null, null, null, page: 1, pageSize: 1);
        Assert.Equal(100, daraltilmis.TotalCount);
    }

    /// <summary>
    /// İKİ ORTAM SÖZLEŞMESİ: ekranlar sayıyı dönen satır listesinden DEĞİL, gerçek toplamdan almalı
    /// ve tavana takıldığında kullanıcıyı uyarmalı.
    /// </summary>
    [Fact]
    public void Ekranlar_GercekToplami_Kullanir_VeKullaniciyiUyarir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln"))) d = d.Parent;
        Assert.NotNull(d);
        string Oku(params string[] p) => File.ReadAllText(Path.Combine(new[] { d!.FullName }.Concat(p).ToArray()));

        var masaustu = Oku("src", "DepoWise.Desktop", "ViewModels", "StockMovementsViewModel.cs");
        Assert.Contains("SearchMovementsGrid", masaustu);
        Assert.Contains("en yenisinden", masaustu);
        Assert.DoesNotContain("$\"{Movements.Count} hareket\"", masaustu);   // eski, yanıltıcı etiket

        var web = Oku("src", "DepoWise.Web", "Components", "Pages", "StockMovements.razor");
        Assert.Contains("/api/stock/movements/grid?page=1&pageSize=1", web);
        Assert.Contains("_gercekToplam", web);
        Assert.Contains("en yenisinden", web);
    }

    public void Dispose() { try { File.Delete(_dbPath); } catch { } }
}
