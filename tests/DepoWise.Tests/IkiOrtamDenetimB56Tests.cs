using DepoWise.Application.Ui;
using DepoWise.Infrastructure.Operations;
using Xunit;

namespace DepoWise.Tests;

/// <summary>
/// ═══ AR-GE B5+B6 İKİ ORTAM DENETİMİ (2026-10-24) — bulunan 10 ayrışmanın düzeltmeleri geri kaymasın ═══
///  ID1 — M-34 toplu yakıt onay metni TEK kaynak (masaüstü FuelService = web ortak dosya); web toplu kayıtta "Tüm Şubeler" kapısı
///  ID2 — M-33 web tekli onayda onay penceresi; M-41 web belge türleri/etiketi tek kaynak (PeriyodikKontrolTests PK5'te)
///  ID3 — M-48 web taşıma sonrası boş değer formu ezmez; M-39 web araç değişince kira alanları sıfırlanır
///  ID4 — M-46/M-47 web: geciken iade uyarısı ve personel zimmet paneli görme yetkisiyle (yazma bloğu dışında)
///  ID5 — M-26 / M-46 masaüstü: düğmeler yetkiyle görünür (web ile aynı)
/// </summary>
public class IkiOrtamDenetimB56Tests
{
    private static string Kok()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DepoWise.sln")) && !File.Exists(Path.Combine(d.FullName, "DepoWise.slnx"))) d = d.Parent;
        return d!.FullName;
    }

    private static string Oku(string rel) => File.ReadAllText(Path.Combine(Kok(), rel));
    private const string W = "src/DepoWise.Web/Components/Pages/", D = "src/DepoWise.Desktop/";

    [Fact]
    public void ID1_Toplu_Yakit_Tek_Metin_Ve_Sube_Kapisi()
    {
        Assert.Equal(TopluYakitMetni.Onay(3, 120m, 4800m, 1), FuelService.TopluOnayMetni(3, 120m, 4800m, 1));
        Assert.Contains("⚠ 1 satırda uyarı var", TopluYakitMetni.Onay(3, 120m, 4800m, 1));
        Assert.DoesNotContain("uyarı var", TopluYakitMetni.Onay(3, 120m, 4800m, 0));
        var fuel = Oku(W + "Fuel.razor");
        Assert.Contains("DepoWise.Application.Ui.TopluYakitMetni.Onay(", fuel);
        var topluBas = fuel.IndexOf("private async Task TopluKaydet()", StringComparison.Ordinal);
        Assert.True(topluBas > 0);
        var topluGovde = fuel.Substring(topluBas, 400);
        Assert.Contains("Dialog.RequireBranchAsync(Auth, \"Yakıt\")", topluGovde);
    }

    [Fact]
    public void ID2_Web_Tekli_Onayda_Onay_Penceresi()
    {
        var t = Oku(W + "Approvals.razor");
        var bas = t.IndexOf("private async Task Onayla(Satir r)", StringComparison.Ordinal);
        Assert.True(bas > 0);
        Assert.Contains("Dialog.Confirm(", t.Substring(bas, 300));
    }

    [Fact]
    public void ID3_Ekipman_Tasima_Ve_Kira_Form_Durumu()
    {
        var e = Oku(W + "Equipment.razor");
        Assert.Contains("if (_tasimaSube is not null) _branchId = _tasimaSube;", e);
        Assert.DoesNotContain("_branchId = _tasimaSube; _location = _tasimaKonum;", e);
        Assert.Contains("_kiraCari = null; _kiraYon = \"in\";", Oku(W + "Vehicles.razor"));
    }

    [Fact]
    public void ID4_Web_Zimmet_Gorunurlugu_Gorme_Yetkisiyle()
    {
        var a = Oku(W + "Assignments.razor");
        var yazmaBlogu = a.IndexOf("@if (Auth.CanCreate(\"assignments\"))", StringComparison.Ordinal);
        var uyari = a.IndexOf("@if (_gecikenler.Count > 0)", StringComparison.Ordinal);
        var blokSonu = a.IndexOf("\n}\n", yazmaBlogu, StringComparison.Ordinal);
        if (blokSonu < 0) blokSonu = a.IndexOf("\r\n}\r\n", yazmaBlogu, StringComparison.Ordinal);
        Assert.True(yazmaBlogu > 0 && uyari > blokSonu, "geciken iade uyarısı yazma bloğunun DIŞINDA olmalı");

        var p = Oku(W + "Personnel.razor");
        var panel = p.IndexOf("@if (_zimmetKisiId is not null && Auth.CanView(\"assignments\"))", StringComparison.Ordinal);
        var form = p.IndexOf("@if (Auth.CanCreate(\"personnel\"))", StringComparison.Ordinal);
        Assert.True(panel > 0 && panel < form, "zimmet paneli düzenleme formundan önce ve ondan bağımsız olmalı");
        Assert.Contains("OnClick=\"@(() => ZimmetPaneliAc(Str(context, \"id\"), Str(context, \"fullName\")))\">Zimmetler", p);
        Assert.Contains("new { personnelId = _zimmetKisiId }", p);
    }

    [Fact]
    public void ID5_Masaustu_Dugmeler_Yetkiyle()
    {
        var ro = Oku(D + "Views/RequestOperationsView.axaml");
        Assert.Contains("Command=\"{Binding StoktanGonderCommand}\" IsVisible=\"{Binding KarsilamaYapabilir}\"", ro);
        Assert.Contains("Command=\"{Binding SiparisAcCommand}\" IsVisible=\"{Binding KarsilamaYapabilir}\"", ro);
        Assert.Contains("<ctrl:FormField Label=\"Tedarikçi (sipariş için)\" IsVisible=\"{Binding KarsilamaYapabilir}\">", ro);   // başlık/liste salt bilgi olarak HasKarsilama ile kalır
        Assert.Contains("public bool KarsilamaYapabilir => HasKarsilama && CanEdit;", Oku(D + "ViewModels/RequestOperationsViewModel.cs"));
        Assert.Contains("Command=\"{Binding IadeTarihiVerCommand}\" IsVisible=\"{Binding CanEdit}\"", Oku(D + "Views/AssignmentsView.axaml"));
    }
}
